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
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);   // leitura das respostas

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
        // 1–2. Configuração do tenant: qual parte é a nossa, o parceiro e os códigos da plataforma (design D4, D5). Falta
        //      algo → DispatchRejectedException antes de qualquer requisição (ADR-0026).
        AvalaraOutboundSettings settings = AvalaraOutboundSettings.Read(context.TenantId, await _profiles.GetAsync(context.TenantId, ct));
        (Party own, Party partner) = settings.PartiesOf(document);
        AvalaraCompanyCodes codes = settings.CodesFor(own.TaxId);

        // 3. Mapeamento: o que o contrato não consegue representar recusa o envio de uma vez, com a lista completa.
        AvalaraMapping mapping = GoodsInvoiceToAvalara.Map(document, new AvalaraHeaderData(codes, partner, context.NaturalKey));
        if (mapping.Document is not { } payload)
        {
            throw new DispatchRejectedException($"Contrato do destino: {string.Join("; ", mapping.Problems)}");
        }

        // 4. Foto do destino (ADR-0006): o payload no formato Avalara, antes do envio. A foto do
        //    domínio é responsabilidade da esteira; aqui só o artefato que este adapter produz.
        await _trace.SaveOutboundAsync(context.TenantId, context.NaturalKey, Destination, JsonSerializer.Serialize(payload, AvalaraJson.Options), ct);

        // 5. POST.
        Uri baseUri = BaseOf(settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, _options.DocumentsPath))
        {
            Content = JsonContent.Create(payload, options: AvalaraJson.Options),
        };
        await ApplyAuthAsync(request, context.TenantId, ct);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);

        // Recusa de conteúdo: permanente — registrada com o motivo da plataforma, sem retentativa (ADR-0026). O
        // resto (5xx, 429, 401/403, 404) segue como exceção, para o retry nativo e a dead-letter (ADR-0004).
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            string refusal = await response.Content.ReadAsStringAsync(ct);
            string omitted = mapping.Omissions.Count == 0 ? string.Empty : $" | Enviado sem: {string.Join("; ", mapping.Omissions)}";
            throw new DispatchRejectedException(
                $"Plataforma de compliance recusou: {PlatformMessage.Extract(refusal, (int)response.StatusCode)}{omitted}");
        }

        response.EnsureSuccessStatusCode();

        // 6. Recibo, com o que o destino não levou.
        AvalaraSubmitResponse body = await ReadJsonAsync<AvalaraSubmitResponse>(response, ct);
        string externalId = body.Id
            ?? throw new InvalidOperationException("Resposta de envio da plataforma sem identificador externo.");

        return new IntegrationReceipt { ExternalId = externalId, Status = IntegrationStatus.Submitted, Omissions = mapping.Omissions };
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
    {
        Uri baseUri = BaseOf(AvalaraOutboundSettings.Read(context.TenantId, await _profiles.GetAsync(context.TenantId, ct)));
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

        using JsonDocument body = await ReadJsonDocumentAsync(response, ct);
        IntegrationStatus status = Translate(StringProperty(body.RootElement, "status"));

        // O status nativo nunca sai do adapter; o que atravessa é o texto da plataforma (ADR-0003, refinado pelo
        // ADR-0026). Sem mensagem reconhecida, diz isso — e não devolve o corpo, que traz o status nativo.
        string? message = status == IntegrationStatus.IntegrationError
            ? PlatformMessage.FindMessages(body.RootElement) is { } reason
                ? $"Plataforma de compliance rejeitou: {reason}"
                : "Plataforma de compliance rejeitou sem informar a causa."
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
    // perfil ou settings, cai na config do adapter — a consulta de status não depende dos códigos da empresa.
    // Em produção o secret/token seguem o mesmo padrão, resolvidos no Key Vault pelas referências das settings.
    private Uri BaseOf(AvalaraOutboundSettings settings)
        => new(string.IsNullOrWhiteSpace(settings.BaseUrl) ? _options.BaseUrl : settings.BaseUrl, UriKind.Absolute);

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

    // A consulta de status é lida como JSON cru: além do status, o motivo da recusa vem em formato ainda não gravado.
    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string raw = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("Resposta da plataforma de compliance vazia.");
        }

        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Resposta da plataforma de compliance não é um JSON válido.", ex);
        }
    }

    // Leitura sem distinguir maiúscula, como a desserialização Web que o adapter usava.
    private static string? StringProperty(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object
            && root.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.String } property
                ? property.Value.GetString()
                : null;

    // Resposta nativa do envio — internal: o formato externo fica preso no adapter.
    private sealed record AvalaraSubmitResponse
    {
        public string? Id { get; init; }
    }
}
