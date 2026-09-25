using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Núcleo do worker de feed de mudanças (ADR-0024): a cada passada, para cada tenant com o adapter da
/// origem e o poll ligado, vencido o intervalo, toma o lease, lê a marca d'água, puxa o delta com
/// sobreposição, enfileira cada referência na fila de descoberta e avança a marca página a página.
/// Falha não avança a marca; a próxima passada repete. Lógica pura — um BackgroundService só chama
/// <see cref="RunOnceAsync"/> num timer.
/// </summary>
public sealed class ChangeFeedPoller
{
    private readonly IDocumentChangeFeed _feed;
    private readonly IConnectorProfileStore _profiles;
    private readonly IChangeFeedCursorStore _cursors;
    private readonly ILeaseStore _leases;
    private readonly IDocumentQueue _queue;
    private readonly ChangeFeedPollerOptions _options;
    private readonly TimeProvider _clock;

    public ChangeFeedPoller(
        IDocumentChangeFeed feed,
        IConnectorProfileStore profiles,
        IChangeFeedCursorStore cursors,
        ILeaseStore leases,
        IDocumentQueue queue,
        ChangeFeedPollerOptions options,
        TimeProvider clock)
    {
        _feed = feed;
        _profiles = profiles;
        _cursors = cursors;
        _leases = leases;
        _queue = queue;
        _options = options;
        _clock = clock;
    }

    /// <summary>Faz uma passada por todos os tenants da origem e devolve o resumo.</summary>
    public async Task<ChangeFeedPassSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var pass = new PassTally();
        IReadOnlyList<TenantConnectorProfile> profiles = await _profiles.ListByInboundAdapterAsync(_feed.Origin, ct);

        foreach (TenantConnectorProfile profile in profiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await PollTenantAsync(profile, pass, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Um tenant com problema (inclusive no próprio store) não derruba a passada dos outros.
                pass.Failures[profile.TenantId] = ex.Message;
            }
        }

        return pass.ToSummary();
    }

    private async Task PollTenantAsync(TenantConnectorProfile profile, PassTally pass, CancellationToken ct)
    {
        string tenant = profile.TenantId;

        // Settings inválidas não impedem a checagem: viram a falha registrada do tenant, respeitando o
        // intervalo padrão (senão cada tick de 15s contaria mais uma falha).
        ChangeFeedPollSettings? settings = null;
        ConnectorSettingsException? settingsError = null;
        try
        {
            settings = ChangeFeedPollSettings.Parse(profile.InboundSettings);
        }
        catch (ConnectorSettingsException ex)
        {
            settingsError = ex;
        }

        if (settings is { Enabled: false })
        {
            return;
        }

        TimeSpan interval = settings?.Interval ?? ChangeFeedPollSettings.DefaultInterval;
        if (!IsDue(await _cursors.GetAsync(tenant, _feed.Origin, ct), interval))
        {
            return;
        }

        var lease = new LeaseClaim($"changefeed:{_feed.Origin}:{tenant}", _options.OwnerId);
        if (!await _leases.TryAcquireAsync(lease.Resource, lease.Owner, _options.LeaseTtl, ct))
        {
            pass.LeasesBusy++;
            return;
        }

        try
        {
            // Relê sob o lease: outra réplica pode ter terminado um poll entre a checagem e a tomada.
            if (!IsDue(await _cursors.GetAsync(tenant, _feed.Origin, ct), interval))
            {
                return;
            }

            try
            {
                if (settingsError is not null)
                {
                    throw settingsError;
                }

                await PullAsync(tenant, settings!, lease, pass, ct);
            }
            catch (LeaseLostException)
            {
                // A marca desta página não foi gravada; a réplica dona do lease segue. Não é falha do tenant.
                pass.LeasesLost.Add(tenant);
            }
            catch (ChangeFeedThrottledException ex) when (!ct.IsCancellationRequested)
            {
                DateTimeOffset now = _clock.GetUtcNow();
                await _cursors.RecordFailureAsync(tenant, _feed.Origin, now, ex.Message, now + ex.RetryAfter, ct);
                pass.Failures[tenant] = ex.Message;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await _cursors.RecordFailureAsync(tenant, _feed.Origin, _clock.GetUtcNow(), ex.Message, notBefore: null, ct);
                pass.Failures[tenant] = ex.Message;
            }
        }
        finally
        {
            // Liberar sempre, até no desligamento: senão o tenant fica travado até o prazo do lease vencer.
            await _leases.ReleaseAsync(lease.Resource, lease.Owner, CancellationToken.None);
        }
    }

    private async Task PullAsync(string tenant, ChangeFeedPollSettings settings, LeaseClaim lease, PassTally pass, CancellationToken ct)
    {
        ChangeFeedCursor cursor = await _cursors.StartAsync(tenant, _feed.Origin, settings.StartFrom ?? _clock.GetUtcNow(), ct);
        DateTimeOffset start = cursor.Watermark!.Value;
        DateTimeOffset watermark = start;
        int pages = 0;
        bool capped = false;

        pass.TenantsPolled++;

        // A sobreposição é daqui, não do adapter: a marca é nossa, o relógio é da origem.
        await foreach (ChangeFeedPage page in _feed.PullAsync(tenant, watermark - settings.Overlap, ct).WithCancellation(ct))
        {
            // Enfileira a página inteira ANTES de avançar a marca: uma falha aqui repete a página, nunca a pula.
            foreach (DocumentReference reference in page.References)
            {
                await _queue.EnqueueAsync(reference with { Trigger = IngestionTrigger.Event }, ct);
                pass.ReferencesEnqueued++;
            }

            // Renovar só mantém o lease vivo numa leitura longa; quem protege o cursor é o avanço condicionado.
            if (!await _leases.RenewAsync(lease.Resource, lease.Owner, _options.LeaseTtl, ct))
            {
                throw new LeaseLostException();
            }

            if (page.HighWatermark is { } high && high > watermark)
            {
                if (!await _cursors.TryAdvanceWatermarkAsync(tenant, _feed.Origin, high, lease, ct))
                {
                    throw new LeaseLostException();
                }

                watermark = high;
            }

            if (++pages >= _options.MaxPagesPerPass)
            {
                capped = true;
                break;
            }
        }

        await _cursors.RecordSuccessAsync(tenant, _feed.Origin, _clock.GetUtcNow(), ct);

        if (capped && watermark <= start)
        {
            pass.Stalled.Add(tenant);
        }
    }

    private bool IsDue(ChangeFeedCursor? cursor, TimeSpan interval)
    {
        if (cursor is null)
        {
            return true;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        bool intervalElapsed = cursor.LastPolledAt is null || now >= cursor.LastPolledAt.Value + interval;
        bool throttleOver = cursor.NotBefore is null || now >= cursor.NotBefore.Value;
        return intervalElapsed && throttleOver;
    }

    /// <summary>O lease deixou de ser desta réplica no meio do poll.</summary>
    private sealed class LeaseLostException : Exception;

    private sealed class PassTally
    {
        public int TenantsPolled { get; set; }
        public int ReferencesEnqueued { get; set; }
        public int LeasesBusy { get; set; }
        public List<string> LeasesLost { get; } = [];
        public Dictionary<string, string> Failures { get; } = [];
        public List<string> Stalled { get; } = [];

        public ChangeFeedPassSummary ToSummary() => new()
        {
            TenantsPolled = TenantsPolled,
            ReferencesEnqueued = ReferencesEnqueued,
            LeasesBusy = LeasesBusy,
            LeasesLost = LeasesLost,
            Failures = Failures,
            Stalled = Stalled,
        };
    }
}
