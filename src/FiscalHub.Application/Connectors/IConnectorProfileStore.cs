namespace FiscalHub.Application.Connectors;

/// <summary>Persiste o perfil de conector por tenant. Implementada na Infrastructure.</summary>
public interface IConnectorProfileStore
{
    Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default);

    Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default);
}
