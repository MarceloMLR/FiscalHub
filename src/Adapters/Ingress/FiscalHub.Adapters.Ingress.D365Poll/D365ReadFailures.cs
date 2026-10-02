using System.Net;
using Azure.Identity;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>O que uma leitura do F&amp;O lê, para o motivo: a descrição, a entidade e o privilégio de leitura dela na role.</summary>
internal sealed record D365Read(string Description, string Entity, string Privilege);

/// <summary>
/// Traduz a falha de uma leitura do F&amp;O feita a pedido de uma tela (o diretório e a descoberta por período, change
/// erp-company-directory-and-card-filters, D1) num motivo curto e seguro, a <see cref="OriginUnavailableException"/>. Do F&amp;O
/// entra só o status; do Entra ID, nada. Nunca o token, o segredo, o corpo da resposta nem um cabeçalho: o detalhe vai
/// para o log. A configuração que não se lê (<see cref="ConnectorSettingsException"/>) passa como veio, porque o motivo dela
/// já é texto nosso.
/// </summary>
internal static class D365ReadFailures
{
    public static async Task<T> GuardAsync<T>(Func<Task<T>> read, D365Read what, string tenantId, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await read();
        }
        catch (ConnectorSettingsException)
        {
            throw;
        }
        catch (Exception ex) when (Translate(ex, what, ct) is { } reason)
        {
            logger.LogWarning(ex, "Leitura da {Entity} do D365 falhou para o tenant {Tenant}: {Reason}", what.Entity, tenantId, reason);
            throw new OriginUnavailableException(reason, ex);
        }
    }

    private static string? Translate(Exception ex, D365Read what, CancellationToken ct)
    {
        switch (ex)
        {
            case OperationCanceledException when ct.IsCancellationRequested:
                return null;   // quem pediu desistiu: não é falha da origem
            case ChangeFeedThrottledException:
                return $"O F&O pediu espera (throttling) na leitura {what.Description}. Tente de novo em instantes.";
            case HttpRequestException { StatusCode: HttpStatusCode.Forbidden }:
                return $"O F&O negou a leitura {what.Description} (HTTP 403). A role FSFiscalHubIntegration precisa do privilégio "
                    + $"{what.Privilege}, com deploy no ambiente, e a role precisa estar atribuída ao app.";
            case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }:
                return $"O F&O não aceitou o token na leitura {what.Description} (HTTP 401). Confira se o app está cadastrado no "
                    + "F&O, em Aplicativos do Microsoft Entra ID.";
            case HttpRequestException { StatusCode: { } status }:
                return $"O F&O respondeu HTTP {(int)status} à leitura {what.Description}.";
        }

        return NetworkFailure.Of(ex) switch
        {
            NetworkCause.HostNotFound => "O endereço do ambiente F&O não existe. Confira a URL em Configurações → Conectores → Entrada.",
            NetworkCause.Other => $"O F&O, o Entra ID ou o cofre de segredos não respondeu agora, na leitura {what.Description}.",
            // Sem causa de rede, a falha do Azure.Identity é recusa: o código AADSTS vai para o log, e não para a tela.
            _ when ex is AuthenticationFailedException => "A credencial do ERP foi recusada pelo Entra ID. Confira o tenant do Entra, o "
                + "Client ID e o Client Secret em Configurações → Conectores → Entrada.",
            _ => null,
        };
    }
}
