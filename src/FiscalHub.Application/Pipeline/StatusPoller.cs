using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Pipeline;

/// <summary>
/// Fecha o ciclo assíncrono: consulta o status dos documentos em voo pelo GUID externo e persiste o
/// desfecho — confirma, marca erro, ou (se a plataforma não responde após um limite de consultas, o
/// 204 eterno) marca <see cref="IntegrationStatus.Unconfirmed"/>. Lógica pura: um BackgroundService
/// só chama <see cref="PollOnceAsync"/> num timer.
/// </summary>
public sealed class StatusPoller<TDocument>
{
    private readonly IProcessingStore _store;
    private readonly IComplianceDispatcher<TDocument> _dispatcher;
    private readonly StatusPollerOptions _options;

    public StatusPoller(IProcessingStore store, IComplianceDispatcher<TDocument> dispatcher, StatusPollerOptions options)
    {
        _store = store;
        _dispatcher = dispatcher;
        _options = options;
    }

    /// <summary>Faz uma passada: consulta cada documento em voo e grava o desfecho. Devolve quantos consultou.</summary>
    public async Task<int> PollOnceAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PendingIntegration> pending = await _store.ListPendingAsync(_options.BatchSize, ct);

        foreach (PendingIntegration doc in pending)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await PollOneAsync(doc, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Falha ao consultar UM documento não pode derrubar o lote inteiro. Deixa pra
                // próxima passada; se persistir, o limite de tentativas o move pra Unconfirmed.
            }
        }

        return pending.Count;
    }

    private async Task PollOneAsync(PendingIntegration doc, CancellationToken ct)
    {
        var context = new DispatchContext
        {
            TenantId = doc.TenantId,
            NaturalKey = doc.NaturalKey,
            CorrelationId = Guid.NewGuid().ToString(),
            Operation = DocumentStatus.Issued,
        };

        IntegrationResult result = await _dispatcher.CheckStatusAsync(doc.ExternalId, context, ct);
        int attempts = doc.Attempts + 1;

        switch (result.Status)
        {
            case IntegrationStatus.Confirmed:
                await _store.MarkPolledAsync(doc.TenantId, doc.NaturalKey, IntegrationStatus.Confirmed, null, attempts, ct);
                break;

            case IntegrationStatus.IntegrationError:
                await _store.MarkPolledAsync(doc.TenantId, doc.NaturalKey, IntegrationStatus.IntegrationError, result.Message, attempts, ct);
                break;

            default: // ainda em processamento na plataforma (Submitted/Pending)
                bool giveUp = attempts >= _options.MaxAttempts;
                await _store.MarkPolledAsync(
                    doc.TenantId, doc.NaturalKey,
                    giveUp ? IntegrationStatus.Unconfirmed : IntegrationStatus.Submitted,
                    giveUp ? "Sem resposta da plataforma após o limite de consultas." : null,
                    attempts, ct);
                break;
        }
    }
}

/// <summary>Parâmetros do poll: limite de consultas (o 204 eterno) e tamanho do lote por passada.</summary>
public sealed record StatusPollerOptions
{
    /// <summary>Máximo de consultas antes de marcar Unconfirmed.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>Quantos documentos consultar por passada.</summary>
    public int BatchSize { get; init; } = 50;
}
