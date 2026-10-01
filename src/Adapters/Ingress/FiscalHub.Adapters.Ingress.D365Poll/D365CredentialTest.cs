using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Identity;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// O teste da credencial gravada do D365 (change <c>module-navigation-and-integration-panel</c>, D11). Ele não para no
/// token: o token prova a credencial, e não a permissão. Então pega um token NOVO do Entra ID e faz uma leitura mínima, o
/// primeiro registro da <c>FSFiscalDocumentBRs</c>, entre empresas e só com a chave.
/// <list type="bullet">
///   <item><b>O token novo:</b> cada teste usa uma instância nova da credencial
///   (<see cref="ClientCredentialsD365TokenProvider.GetFreshTokenAsync"/>). O cache do coletor não é lido nem trocado.</item>
///   <item><b>A identidade:</b> sempre a credencial do tenant, e nunca o Azure CLI, nem em desenvolvimento. O teste
///   responde sobre a credencial gravada.</item>
///   <item><b>O motivo:</b> texto nosso. Do Entra ID entra só o código <c>AADSTS</c>, e do F&amp;O só o status. Nunca o
///   token, o segredo, nem um cabeçalho.</item>
/// </list>
/// </summary>
internal sealed partial class D365CredentialTest(ClientCredentialsD365TokenProvider tokens, HttpClient http) : IConnectorCredentialTest
{
    public string Adapter => "Dynamics365";

    public ConnectorSettingsKind Side => ConnectorSettingsKind.Inbound;

    public async Task<CredentialTestOutcome> TestAsync(TenantConnectorProfile profile, string? environment, CancellationToken ct = default)
    {
        D365InboundSettings settings;
        try
        {
            settings = D365InboundSettings.Parse(profile.InboundSettings);
        }
        catch (ConnectorSettingsException ex)
        {
            return Incomplete(ex.Message);
        }

        var connection = new D365Connection(profile.TenantId, settings.Url, settings.Auth);
        string token;
        try
        {
            token = await tokens.GetFreshTokenAsync(connection, ct);
        }
        catch (ConnectorSettingsException ex)
        {
            // Auth incompleto, referência fora do tenant, ou o segredo ausente no cofre: nenhuma requisição saiu.
            return Incomplete(ex.Message);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested)
            && NetworkFailure.Of(ex) != NetworkCause.None)
        {
            // Só uma causa de rede na cadeia é indisponibilidade: o Entra ID ou o cofre não responderam.
            return Unavailable("O token não saiu agora: o Entra ID ou o cofre de segredos não respondeu.");
        }
        catch (AuthenticationFailedException ex)
        {
            // Sem causa de rede, é recusa: segredo errado, app ou tenant que não existe. O código AADSTS pode vir só na
            // exceção interna (prova manual, 2026-10-01), e por isso a busca é na cadeia inteira.
            string code = AadstsCode().Match(ex.ToString()) is { Success: true } found ? $" ({found.Value})" : string.Empty;
            return Refused($"O Entra ID recusou a credencial{code}. Confira o tenant do Entra, o Client ID e o Client Secret.");
        }
        catch (ArgumentException)
        {
            // O Azure.Identity valida o formato do tenant do Entra e do Client ID antes de qualquer pedido.
            return Refused("O tenant do Entra ou o Client ID não está num formato válido.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Refused($"O token não saiu ({ex.GetType().Name}).");
        }

        return await ReadAsync(settings.Url, token, ct);
    }

    private async Task<CredentialTestOutcome> ReadAsync(Uri environmentUrl, string token, CancellationToken ct)
    {
        // Um GET só, sem seguir nextLink: basta saber se a entidade responde a este token.
        var url = new Uri($"{environmentUrl.GetLeftPart(UriPartial.Authority)}/data/FSFiscalDocumentBRs?$top=1&$select=FiscalDocumentRecId&cross-company=true");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            // O host do F&O que não existe é URL errada, e não indisponibilidade.
            return NetworkFailure.Of(ex) == NetworkCause.HostNotFound
                ? Refused("O endereço do ambiente F&O não existe. Confira a URL.")
                : Unavailable("O Entra ID emitiu um token novo, mas o F&O não respondeu agora (sem resposta).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unavailable("O Entra ID emitiu um token novo, mas o F&O não respondeu a tempo.");
        }

        using (response)
        {
            int status = (int)response.StatusCode;
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await HasRecordAsync(response, ct)
                        ? Worked("O Entra ID emitiu um token novo, e a FSFiscalDocumentBRs respondeu com uma nota.")
                        : Worked("O Entra ID emitiu um token novo, e a FSFiscalDocumentBRs respondeu sem nota. Entre empresas, a "
                            + "leitura vazia não prova o acesso às empresas: a falta de acesso a uma empresa também devolve vazio.");
                case HttpStatusCode.Unauthorized:
                    return Refused($"O Entra ID emitiu o token, mas o F&O não o aceitou (HTTP {status}). Confira se o app está "
                        + "cadastrado no F&O, em Aplicativos do Microsoft Entra ID.");
                case HttpStatusCode.Forbidden:
                    return Refused($"O F&O aceitou o token, mas o app não tem o privilégio de leitura das entidades FS (HTTP {status}). "
                        + "Confira a role do pacote FS do usuário do app.");
                case HttpStatusCode.NotFound:
                    return Refused($"A FSFiscalDocumentBRs não existe neste ambiente (HTTP {status}): o pacote FS não está implantado.");
                case HttpStatusCode.TooManyRequests:
                    return Unavailable($"O F&O pediu para esperar (HTTP {status}). Tente de novo em instantes.");
                default:
                    return status >= 500
                        ? Unavailable($"O F&O não respondeu agora (HTTP {status}). Tente de novo em instantes.")
                        : Refused($"O F&O recusou a leitura da FSFiscalDocumentBRs (HTTP {status}).");
            }
        }
    }

    private static async Task<bool> HasRecordAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument page = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return page.RootElement.TryGetProperty("value", out JsonElement value)
                && value.ValueKind == JsonValueKind.Array
                && value.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CredentialTestOutcome Worked(string reason) => new(CredentialTestVerdict.Worked, reason);

    private static CredentialTestOutcome Refused(string reason) => new(CredentialTestVerdict.Refused, reason);

    private static CredentialTestOutcome Unavailable(string reason) => new(CredentialTestVerdict.Unavailable, reason);

    private static CredentialTestOutcome Incomplete(string reason) => new(CredentialTestVerdict.Incomplete, reason);

    [GeneratedRegex(@"AADSTS\d+")]
    private static partial Regex AadstsCode();
}
