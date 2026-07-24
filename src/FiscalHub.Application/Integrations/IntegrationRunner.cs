using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Integrations;

/// <summary>
/// Núcleo do disparo de integração, sem conhecer HTTP nem agendador. Descobre as notas do período,
/// enfileira cada referência e registra a execução. O manual fura a idempotência (intenção
/// explícita do usuário); o agendado é rede de segurança e dedupa por conteúdo (ADR-0016).
/// </summary>
public sealed class IntegrationRunner : IIntegrationRunner
{
    private readonly IDocumentDiscovery _discovery;
    private readonly IDocumentQueue _queue;
    private readonly IExecutionStore _executions;

    public IntegrationRunner(IDocumentDiscovery discovery, IDocumentQueue queue, IExecutionStore executions)
    {
        _discovery = discovery;
        _queue = queue;
        _executions = executions;
    }

    public async Task<int> RunAsync(RunRequest request, CancellationToken ct = default)
    {
        var criteria = new DiscoveryCriteria
        {
            TenantId = request.TenantId,
            Start = request.PeriodStart,
            End = request.PeriodEnd,
            Company = request.CompanyCode,
            Establishment = request.BranchCode,
        };

        IReadOnlyList<DocumentReference> found = await _discovery.DiscoverAsync(criteria, ct);

        // Manual = recarga explícita (fura idempotência). Agendado = rede de segurança (dedupe por
        // conteúdo, não reintegra o que o tempo-real já resolveu).
        IngestionTrigger trigger = request.Mode == IntegrationMode.Manual
            ? IngestionTrigger.Manual
            : IngestionTrigger.Event;

        foreach (DocumentReference reference in found)
        {
            await _queue.EnqueueAsync(reference with { Trigger = trigger }, ct);
        }

        await _executions.RecordAsync(new IntegrationExecution
        {
            Mode = request.Mode,
            TenantId = request.TenantId,
            CompanyCode = request.CompanyCode,
            BranchCode = request.BranchCode,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            DiscoveredCount = found.Count,
            ScheduleId = request.ScheduleId,
        }, ct);

        return found.Count;
    }
}
