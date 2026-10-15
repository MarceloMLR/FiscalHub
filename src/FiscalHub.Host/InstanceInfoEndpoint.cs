using System.Security.Claims;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Host;

/// <summary>
/// O <c>/info</c>, num ponto único que o <c>Program.cs</c> chama e que o teste de papel sobe (design D8) — a mesma razão
/// das rotas das fotos cruas: o que o teste prova é o registro de verdade, e não uma cópia dele.
/// </summary>
public static class InstanceInfoEndpoint
{
    /// <summary>
    /// O ambiente da instância e a integração automática do tenant logado. O ambiente é o da instância, sem padrão
    /// (change <c>platform-account-and-connectors</c>, D6). A integração automática não é campo gravado: é derivada do
    /// perfil (adapter que varre e <c>poll.enabled</c>), com as origens dos feeds registrados, as mesmas que o poller
    /// consome (ADR-0029). O <c>inboundScans</c> diz se o adapter varre: é o que faz o selo aparecer, verde ou vermelho,
    /// ou sumir. A árvore de conectores monta a barra lateral, para qualquer papel: apresentação, e não permissão — ela
    /// traz os nomes dos produtos que o próprio tenant contratou, e não credencial, endereço nem segredo. O rótulo e o
    /// agendamento de cada conector saem das declarações registradas, e não do perfil (<c>module-navigation</c>; D6).
    /// <para>O <c>canViewRawJson</c> é a exceção: ele é <b>permissão</b>, e sai da mesma lista do servidor que o
    /// <c>RequireRole</c> das fotos cruas usa (<see cref="RawTraceRoles"/>; design D7). O dashboard não tem lista
    /// própria — ele pergunta aqui.</para>
    /// </summary>
    public static IEndpointRouteBuilder MapInstanceInfoEndpoint(this IEndpointRouteBuilder app, RawTraceRoles? roles = null)
    {
        RawTraceRoles rawTrace = roles ?? app.ServiceProvider.GetService<RawTraceRoles>() ?? RawTraceRoles.Default;

        app.MapGet("/info", async (
            ClaimsPrincipal user, IConnectorProfileStore profiles, IEnumerable<IDocumentChangeFeed> feeds,
            IEnumerable<IAdapterDeclaration> declarations, IInstanceEnvironment instance, ITenantContext tenant,
            CancellationToken ct) =>
            Results.Ok(InstanceInfo.For(
                instance,
                await profiles.GetAsync(tenant.TenantId, ct),
                declarations,
                feeds.Select(f => f.Origin),
                rawTrace.Allows(user))));

        return app;
    }
}
