namespace FiscalHub.Application.Admin;

/// <summary>Leitura e edição do cadastro do tenant corrente (nome, CNPJ).</summary>
public interface ITenantAdminService
{
    Task<TenantView?> GetAsync(string tenantId, CancellationToken ct = default);

    Task<AdminResult<TenantView>> UpdateAsync(string tenantId, UpdateTenantInput input, CancellationToken ct = default);
}
