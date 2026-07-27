namespace FiscalHub.Application.Auth;

/// <summary>
/// O tenant da requisição atual (vem do claim do usuário logado). As consultas de leitura o usam
/// pra escopar os dados — cada usuário só enxerga o que é do seu tenant.
/// </summary>
public interface ITenantContext
{
    string TenantId { get; }
}
