using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FiscalHub.Application.Outbound;
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

    public AvalaraComplianceDispatcher(
        HttpClient http,
        IOptions<AvalaraOptions> options,
        IAvalaraTokenProvider tokenProvider)
    {
        _http = http;
        _options = options.Value;
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc/>
    public string Destination => _options.Destination;

    /// <inheritdoc/>
    public async Task<IntegrationReceipt> SubmitAsync(GoodsInvoice document, DispatchContext context, CancellationToken ct = default)
    {
        AvalaraDocument payload = GoodsInvoiceToAvalara.Map(document);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.DocumentsPath)
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
        var path = $"{_options.DocumentsPath}/{Uri.EscapeDataString(externalId)}/status";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        await ApplyAuthAsync(request, context.TenantId, ct);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
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
