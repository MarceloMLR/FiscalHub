using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FiscalHub.Application.Support;
using FiscalHub.Application.Tracing;

namespace FiscalHub.Host;

/// <summary>
/// Quem vê as fotos cruas — o <c>/trace</c> e o zip do documento. <b>Um lugar só no servidor</b>
/// (<c>platform-response-trace</c>; ADR-0039 §5): daqui saem o <c>RequireRole</c> das duas rotas e o
/// <c>canViewRawJson</c> do <c>/info</c>, e o dashboard não tem cópia da lista — ele pergunta ao <c>/info</c>.
/// <para>Hoje só o Admin. Um papel de Suporte, quando existir (ADR-0030), entra aqui e no <c>UserRole</c>, e os botões
/// aparecem para ele sem nenhuma mudança no dashboard.</para>
/// </summary>
public sealed class RawTraceRoles
{
    public RawTraceRoles(IReadOnlyList<string> roles) => Roles = roles;

    /// <summary>A lista do servidor. É esta que vale; mudar aqui muda a rota e o <c>/info</c> juntos.</summary>
    public static RawTraceRoles Default { get; } = new(["Admin"]);

    public IReadOnlyList<string> Roles { get; }

    /// <summary>Se o usuário logado pode ver as fotos cruas. É a pergunta que o <c>/info</c> responde (design D7).</summary>
    public bool Allows(ClaimsPrincipal user) => Roles.Any(user.IsInRole);
}

/// <summary>
/// As rotas das fotos cruas, num ponto único que o <c>Program.cs</c> chama e que o teste de papel sobe (design D8).
/// Apagar um <c>RequireRole</c> daqui quebra o teste; parar de chamar a extensão quebra o build, porque as rotas somem.
/// </summary>
public static class RawTraceEndpoints
{
    /// <summary>
    /// A mesma resposta para "é de outro tenant" e "não tem fotos": não se confirma a existência de nota alheia
    /// (<c>tenant-boundary</c>). A primeira vista do detalhe (<c>/reading</c>) usa a mesma mensagem.
    /// </summary>
    public const string NoTraceMessage = "sem fotos para esse documento.";

    public static IEndpointRouteBuilder MapRawTraceEndpoints(this IEndpointRouteBuilder app, RawTraceRoles? roles = null)
    {
        string[] allowed = [.. (roles ?? app.ServiceProvider.GetService<RawTraceRoles>() ?? RawTraceRoles.Default).Roles];

        // Fotos de rastreabilidade de um documento (fonte, domínio, destino, respostas), para o JSON cru do dashboard. Só
        // para quem tem o papel, e só o tenant de quem está logado (ADR-0028): outro tenant e documento sem fotos têm o
        // mesmo 404 — não se confirma nota alheia. O papel recusa antes de qualquer leitura do armazenamento.
        app.MapGet("/trace/{tenantId}/{naturalKey}", async (string tenantId, string naturalKey, DocumentTraceQuery traces, CancellationToken ct) =>
        {
            IReadOnlyList<TraceFile>? files = await traces.GetAsync(tenantId, naturalKey, ct);
            if (files is null)
            {
                return Results.NotFound(new { message = NoTraceMessage });
            }

            // JSON entra aninhado (legível); a fonte crua (XML) entra como string. A foto repetida em dois períodos
            // (reprocesso em outro mês) vem por último na listagem e prevalece.
            var snapshots = new Dictionary<string, object>();
            foreach (TraceFile file in files)
            {
                snapshots[file.Name] = file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Deserialize<JsonElement>(file.Content)
                    : Encoding.UTF8.GetString(file.Content);
            }

            return Results.Ok(snapshots);
        }).RequireAuthorization(policy => policy.RequireRole(allowed));

        // Download: zipa as fotos de um documento pra baixar de uma vez, com a mesma regra de papel e de tenant do /trace.
        app.MapGet("/documents/{tenantId}/{naturalKey}/download", async (string tenantId, string naturalKey, DocumentTraceQuery traces, CancellationToken ct) =>
        {
            IReadOnlyList<TraceFile>? files = await traces.GetAsync(tenantId, naturalKey, ct);
            return files is null
                ? Results.NotFound(new { message = NoTraceMessage })
                : Results.File(TraceArchive.Zip(files), "application/zip", $"{naturalKey}.zip");
        }).RequireAuthorization(policy => policy.RequireRole(allowed));

        return app;
    }
}
