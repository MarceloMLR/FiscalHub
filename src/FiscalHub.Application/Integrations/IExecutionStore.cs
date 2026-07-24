namespace FiscalHub.Application.Integrations;

/// <summary>Persiste as execuções de integração (manual/agendada). Implementada na Infrastructure.</summary>
public interface IExecutionStore
{
    Task RecordAsync(IntegrationExecution execution, CancellationToken ct = default);
}
