namespace FiscalHub.Application.Connectors;

/// <summary>Persiste o perfil de conector por tenant. Implementada na Infrastructure.</summary>
public interface IConnectorProfileStore
{
    Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default);

    Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Perfis de todos os tenants que usam o adapter de entrada informado. Consulta de sistema (não é
    /// escopada ao tenant logado): o worker de feed de mudanças varre os tenants por aqui.
    /// </summary>
    Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default);
}
