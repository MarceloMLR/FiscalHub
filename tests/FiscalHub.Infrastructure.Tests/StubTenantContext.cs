using FiscalHub.Application.Auth;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>Tenant fixo pros testes das consultas escopadas.</summary>
internal sealed class StubTenantContext(string tenantId) : ITenantContext
{
    public string TenantId => tenantId;
}
