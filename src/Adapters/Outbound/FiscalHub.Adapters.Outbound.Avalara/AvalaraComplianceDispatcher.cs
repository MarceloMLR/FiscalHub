using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Despacha uma <see cref="GoodsInvoice"/> para a plataforma de compliance (Avalara) por HTTP.
/// Reusa o mapeamento testado (<see cref="GoodsInvoiceToAvalara"/>) e traduz o status nativo da
/// plataforma para o <see cref="IntegrationStatus"/> comum (camada anticorrupção — ADR-0003).
/// </summary>
internal sealed class AvalaraComplianceDispatcher : IComplianceDispatcher<GoodsInvoice>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AvalaraOptions _options;
    private readonly IAvalaraTokenProvider _tokenProvider;
    private readonly IProcessingTrace _trace;
    private readonly IConnectorProfileStore _profiles;

    public AvalaraComplianceDispatcher(
        HttpClient http,
        IOptions<AvalaraOptions> options,
        IAvalaraTokenProvider tokenProvider,
        IProcessingTrace trace,
        IConnectorProfileStore profiles)
    {
        _http = http;
        _options = options.Value;
        _tokenProvider = tokenProvider;
        _trace = trace;
        _profiles = profiles;
    }

    /// <inheritdoc/>
    public string Destination => _options.Destination;

    /// <inheritdoc/>
    public async Task<IntegrationReceipt> SubmitAsync(GoodsInvoice document, DispatchContext context, CancellationToken ct = default)
    {
        AvalaraDocument payload = GoodsInvoiceToAvalara.Map(document);

        // Foto do destino (ADR-0006): o payload no formato Avalara, antes do envio. A foto do
        // domínio é responsabilidade da esteira; aqui só o artefato que este adapter produz.
        await _trace.SaveOutboundAsync(context.TenantId, context.NaturalKey, Destination, JsonSerializer.Serialize(payload, JsonOpts), ct);

        Uri baseUri = await ResolveBaseAsync(context.TenantId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, _options.DocumentsPath))
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        await ApplyAuthAsync(request, context.TenantId, ct);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        AvalaraSubmitResponse body = await ReadJsonAsync<AvalaraSubmitResponse>(response, ct);
        string externalId = body.Id
            ?? throw new InvalidOperationException("Resposta de envio da plataforma sem identificador externo.");

        return new IntegrationReceipt { ExternalId = externalId, Status = IntegrationStatus.Submitted };
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
    {
        Uri baseUri = await ResolveBaseAsync(context.TenantId, ct);
        var statusUri = new Uri(baseUri, $"{_options.DocumentsPath}/{Uri.EscapeDataString(externalId)}/status");

        using var request = new HttpRequestMessage(HttpMethod.Get, statusUri);
        await ApplyAuthAsync(request, context.TenantId, ct);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);

        // 204 (sem conteúdo) e 404 (identificador ainda desconhecido) = a plataforma não processou
        // o documento ainda → segue pendente, não é erro. A consulta se repete e, no limite de
        // tentativas, vira Unconfirmed. Assim um GUID problemático não trava o poll do lote.
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return new IntegrationResult { Status = IntegrationStatus.Submitted };
        }

        response.EnsureSuccessStatusCode();

        AvalaraStatusResponse body = await ReadJsonAsync<AvalaraStatusResponse>(response, ct);
        IntegrationStatus status = Translate(body.Status);

        // Mensagem agnóstica de plataforma: nunca vaza o status nativo para fora do adapter.
        string? message = status == IntegrationStatus.IntegrationError
            ? "Integração rejeitada pela plataforma de compliance."
            : null;

        return new IntegrationResult { Status = status, Message = message };
    }

    // Tradução do status nativo da Avalara para o vocabulário comum. Caso desconhecido =
    // ainda em processamento → Submitted (não confirmamos nem damos erro por engano).
    private static IntegrationStatus Translate(string? native) => native?.Trim().ToLowerInvariant() switch
    {
        "carregado" => IntegrationStatus.Confirmed,
        "erro" => IntegrationStatus.IntegrationError,
        _ => IntegrationStatus.Submitted,
    };

    // Resolução por tenant (ADR-0019): a URL base vem do perfil do tenant (ambiente ativo). Sem
    // perfil ou settings, cai na config do adapter. Em produção o secret/token seguem o mesmo padrão,
    // resolvidos no Key Vault pelas referências das settings.
    private async Task<Uri> ResolveBaseAsync(string tenantId, CancellationToken ct)
    {
        TenantConnectorProfile? profile = await _profiles.GetAsync(tenantId, ct);
        string? fromProfile = ExtractBaseUrl(profile);
        string effective = string.IsNullOrWhiteSpace(fromProfile) ? _options.BaseUrl : fromProfile!;
        return new Uri(effective, UriKind.Absolute);
    }

    // Lê a baseUrl do ambiente ativo nas OutboundSettings (schema deste adapter). Settings ausentes
    // ou malformadas → null (cai no fallback). Segredos ficam por referência, resolvidos fora daqui.
    private static string? ExtractBaseUrl(TenantConnectorProfile? profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.OutboundSettings))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(profile.OutboundSettings);
            string envKey = profile.Environment.ToLowerInvariant();   // "sandbox" / "production"
            if (doc.RootElement.TryGetProperty(envKey, out JsonElement env)
                && env.TryGetProperty("baseUrl", out JsonElement url))
            {
                return url.GetString();
            }
        }
        catch (JsonException)
        {
            // Settings malformadas não podem derrubar o envio — usa o fallback.
        }

        return null;
    }

    // Gancho de token: aplica Bearer por-requisição (thread-safe; não mexe no HttpClient compartilhado).
    // Stub no-op devolve cadeia vazia → sem header.
    private async Task ApplyAuthAsync(HttpRequestMessage request, string tenantId, CancellationToken ct)
    {
        string token = await _tokenProvider.GetTokenAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    // Lê o JSON da resposta; um corpo vazio ou malformado vira exceção do adapter (não vaza
    // JsonException da camada de serialização). A falha propaga → o Service Bus reconta (ADR-0004).
    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct)
                ?? throw new InvalidOperationException("Resposta da plataforma de compliance vazia.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Resposta da plataforma de compliance não é um JSON válido.", ex);
        }
    }

    // Respostas nativas da plataforma — internal: o formato externo fica preso no adapter.
    private sealed record AvalaraSubmitResponse
    {
        public string? Id { get; init; }
    }

    private sealed record AvalaraStatusResponse
    {
        public string? Id { get; init; }
        public string? Status { get; init; }
    }
}
