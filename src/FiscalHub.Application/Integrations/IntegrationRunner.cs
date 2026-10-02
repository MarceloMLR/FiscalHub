using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Integrations;

/// <summary>
/// Núcleo do disparo de integração, sem conhecer HTTP nem agendador. Descobre as notas do período, enfileira cada
/// referência e registra a execução. O manual fura a idempotência (intenção explícita do usuário); o agendado é rede de
/// segurança e dedupa por conteúdo (ADR-0016). A descoberta é a do adapter de entrada do perfil do tenant
/// (<see cref="DocumentDiscoveryResolver"/>); sem nenhuma, a execução falha antes de enfileirar ou registrar qualquer coisa.
/// No host, a fila é a de descoberta, a mesma do coletor, para duas cópias da mesma nota não passarem juntas pela checagem
/// de idempotência (change erp-company-directory-and-card-filters, D7).
/// </summary>
public sealed class IntegrationRunner : IIntegrationRunner
{
    private readonly DocumentDiscoveryResolver _discoveries;
    private readonly IDocumentQueue _queue;
    private readonly IExecutionStore _executions;

    public IntegrationRunner(DocumentDiscoveryResolver discoveries, IDocumentQueue queue, IExecutionStore executions)
    {
        _discoveries = discoveries;
        _queue = queue;
        _executions = executions;
    }

    public async Task<int> RunAsync(RunRequest request, CancellationToken ct = default)
    {
        IDocumentDiscovery discovery = await _discoveries.ResolveAsync(request.TenantId, ct);

        var criteria = new DiscoveryCriteria
        {
            TenantId = request.TenantId,
            Start = request.PeriodStart,
            End = request.PeriodEnd,
            Company = request.CompanyCode,
            Establishment = request.BranchCode,
            DocumentNumber = request.DocumentNumber,
        };

        IReadOnlyList<DocumentReference> found = await discovery.DiscoverAsync(criteria, ct);

        // Manual = recarga explícita (fura idempotência). Agendado = rede de segurança (dedupe por
        // conteúdo, não reintegra o que o tempo-real já resolveu).
        IngestionTrigger trigger = request.Mode == IntegrationMode.Manual
            ? IngestionTrigger.Manual
            : IngestionTrigger.Event;

        foreach (DocumentReference reference in found)
        {
            // Trigger define a idempotência; SourceMode é só o rótulo do modo pro dashboard.
            await _queue.EnqueueAsync(reference with { Trigger = trigger, SourceMode = request.Mode.ToString() }, ct);
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
