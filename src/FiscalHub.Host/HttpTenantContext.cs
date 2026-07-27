using System.Security.Claims;
using FiscalHub.Application.Auth;

namespace FiscalHub.Host;

/// <summary>Lê o tenant do claim do usuário logado na requisição atual.</summary>
internal sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public string TenantId =>
        accessor.HttpContext?.User.FindFirstValue("tenant")
        ?? throw new InvalidOperationException("Requisição sem tenant no token.");
}
