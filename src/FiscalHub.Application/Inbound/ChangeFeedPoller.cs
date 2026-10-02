using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Núcleo do worker de feed de mudanças (ADR-0024): a cada passada, para cada tenant com o adapter da
/// origem e o poll ligado, vencido o intervalo, toma o lease, lê a marca d'água, puxa o delta com
/// sobreposição, enfileira cada referência na fila de descoberta e avança a marca página a página.
/// Falha não avança a marca; a próxima passada repete. O par (documento, carimbo) já publicado e assentado
/// não é republicado na releitura da sobreposição (ADR-0025, design D16). Cada referência leva o instante da passada, sem
/// período: é o dia da linha da automática no dashboard (change erp-company-directory-and-card-filters, D15). Lógica pura — um
/// BackgroundService só chama <see cref="RunOnceAsync"/> num timer.
/// </summary>
public sealed class ChangeFeedPoller
{
    private readonly IDocumentChangeFeed _feed;
    private readonly IConnectorProfileStore _profiles;
    private readonly IChangeFeedCursorStore _cursors;
    private readonly ILeaseStore _leases;
    private readonly IDocumentQueue _queue;
    private readonly ChangeFeedPublicationLog _published;
    private readonly ChangeFeedPollerOptions _options;
    private readonly TimeProvider _clock;

    public ChangeFeedPoller(
        IDocumentChangeFeed feed,
        IConnectorProfileStore profiles,
        IChangeFeedCursorStore cursors,
        ILeaseStore leases,
        IDocumentQueue queue,
        ChangeFeedPublicationLog published,
        ChangeFeedPollerOptions options,
        TimeProvider clock)
    {
        _feed = feed;
        _profiles = profiles;
        _cursors = cursors;
        _leases = leases;
        _queue = queue;
        _published = published;
        _options = options;
        _clock = clock;
    }

    /// <summary>Faz uma passada por todos os tenants da origem e devolve o resumo.</summary>
    public async Task<ChangeFeedPassSummary> RunOnceAsync(CancellationToken ct = default)
    {
        var pass = new PassTally { Origin = _feed.Origin };
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
            // Desligado de propósito, calado; desligado por falta da seção, avisado.
            if (!settings.Configured)
            {
                pass.PollNotConfigured.Add(tenant);
            }

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
            ChangeFeedCursor? stored = await _cursors.GetAsync(tenant, _feed.Origin, ct);
            if (!IsDue(stored, interval))
            {
                return;
            }

            try
            {
                if (settingsError is not null)
                {
                    throw settingsError;
                }

                await PullAsync(tenant, settings!, stored, lease, pass, ct);
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

    private async Task PullAsync(
        string tenant, ChangeFeedPollSettings settings, ChangeFeedCursor? stored, LeaseClaim lease, PassTally pass, CancellationToken ct)
    {
        // Cursor sem marca (apagado para o startFrom valer de novo, ou recriado por uma falha): a marca que vai nascer pode
        // ser igual à última vista, e a regressão do BeginPull não a reconheceria. O registro recomeça (design D5).
        if (stored?.Watermark is null)
        {
            _published.Forget(tenant, _feed.Origin);
        }

        ChangeFeedCursor cursor = await _cursors.StartAsync(tenant, _feed.Origin, settings.StartFrom ?? _clock.GetUtcNow(), ct);
        DateTimeOffset start = cursor.Watermark!.Value;
        DateTimeOffset watermark = start;
        int pages = 0;
        bool capped = false;

        pass.TenantsPolled++;

        // A sobreposição é daqui, não do adapter: a marca é nossa, o relógio é da origem.
        DateTimeOffset since = watermark - settings.Overlap;

        // Marca abaixo da última vista = rebobinamento: o registro de publicações zera (quem rebobina quer
        // tudo de volta). E esquece o que a consulta "gt since" não devolve mais.
        _published.BeginPull(tenant, _feed.Origin, watermark, since);

        DateTimeOffset executedAt = _clock.GetUtcNow();

        await foreach (ChangeFeedPage page in _feed.PullAsync(tenant, since, ct).WithCancellation(ct))
        {
            // Enfileira a página inteira ANTES de avançar a marca: uma falha aqui repete a página, nunca a pula.
            // A origem é a do feed (ADR-0025): é por ela que a esteira escolhe o adapter que busca o documento.
            foreach (ChangeFeedItem item in page.Items)
            {
                string key = item.Reference.NaturalKey;

                // Mesmo par, já publicado e assentado: é a sobreposição relendo, nada mudou. A chave inclui o
                // carimbo — alteração real tem carimbo novo e passa. Suprimido conta como enfileirado.
                if (_published.WasPublished(tenant, _feed.Origin, key, item.ChangedAt))
                {
                    pass.ReferencesSuppressed++;
                    continue;
                }

                await _queue.EnqueueAsync(item.Reference with { Trigger = IngestionTrigger.Event, Origin = _feed.Origin, ExecutedAt = executedAt }, ct);
                pass.ReferencesEnqueued++;

                // Só o par assentado entra no registro: acima do horizonte, outra gravação ainda pode ganhar o
                // mesmo carimbo (resolução de segundo), e a próxima passada precisa republicá-lo.
                if (page.StableThrough is { } stable && item.ChangedAt <= stable)
                {
                    _published.Record(tenant, _feed.Origin, key, item.ChangedAt);
                }
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
                _published.Advanced(tenant, _feed.Origin, high);
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
        public int ReferencesSuppressed { get; set; }
        public int LeasesBusy { get; set; }
        public List<string> LeasesLost { get; } = [];
        public Dictionary<string, string> Failures { get; } = [];
        public List<string> Stalled { get; } = [];
        public List<string> PollNotConfigured { get; } = [];
        public string Origin { get; init; } = string.Empty;

        public ChangeFeedPassSummary ToSummary() => new()
        {
            Origin = Origin,
            PollNotConfigured = PollNotConfigured,
            TenantsPolled = TenantsPolled,
            ReferencesEnqueued = ReferencesEnqueued,
            ReferencesSuppressed = ReferencesSuppressed,
            LeasesBusy = LeasesBusy,
            LeasesLost = LeasesLost,
            Failures = Failures,
            Stalled = Stalled,
        };
    }
}
