using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Despacha uma <see cref="GoodsInvoice"/> para a plataforma de compliance (Avalara) por HTTP.
/// Reusa o mapeamento testado (<see cref="GoodsInvoiceToAvalara"/>) e traduz o status nativo da
/// plataforma para o <see cref="IntegrationStatus"/> comum (camada anticorrupção — ADR-0003).
/// <para>Toda resposta da plataforma é lida uma vez, redigida (<see cref="SensitiveText"/>) e fotografada antes de ser
/// classificada: a foto, o motivo e o identificador saem do mesmo texto (ADR-0027).</para>
/// </summary>
internal sealed class AvalaraComplianceDispatcher : IComplianceDispatcher<GoodsInvoice>
{
    private readonly HttpClient _http;
    private readonly AvalaraOptions _options;
    private readonly IAvalaraTokenProvider _tokenProvider;
    private readonly IProcessingTrace _trace;
    private readonly IConnectorProfileStore _profiles;
    private readonly ILogger<AvalaraComplianceDispatcher> _logger;
    private readonly TimeProvider _clock;

    public AvalaraComplianceDispatcher(
        HttpClient http,
        IOptions<AvalaraOptions> options,
        IAvalaraTokenProvider tokenProvider,
        IProcessingTrace trace,
        IConnectorProfileStore profiles,
        ILogger<AvalaraComplianceDispatcher> logger,
        TimeProvider clock)
    {
        _http = http;
        _options = options.Value;
        _tokenProvider = tokenProvider;
        _trace = trace;
        _profiles = profiles;
        _logger = logger;
        _clock = clock;
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

        // 5. POST, com o token do tenant no ambiente ativo. Sem credencial ou sem segredo, a rejeição sai do provider,
        //    antes de qualquer requisição (ADR-0027).
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUri, _options.DocumentsPath))
        {
            Content = JsonContent.Create(payload, options: AvalaraJson.Options),
        };
        AvalaraAccessToken token = await ApplyAuthAsync(request, settings, ct);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);

        // O corpo é lido uma vez, como texto, e redigido: a foto, o motivo e o identificador saem dele. A foto vem antes
        // de classificar, em qualquer status, e por melhor esforço — uma falha nela não pode provocar reenvio.
        (string body, int redactions) = await ReadRedactedAsync(response, token, ct);
        await PhotographAsync(context, TraceExchanges.Submit, request, response, body, redactions, token, ct);
        int status = (int)response.StatusCode;

        switch (response.StatusCode)
        {
            // Recusa de conteúdo: permanente — registrada com o motivo da plataforma, sem retentativa (ADR-0026).
            case HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity:
                string omitted = mapping.Omissions.Count == 0 ? string.Empty : $" | Enviado sem: {string.Join("; ", mapping.Omissions)}";
                throw new DispatchRejectedException($"Plataforma de compliance recusou: {PlatformMessage.Extract(body, status)}{omitted}");

            // A credencial não tem acesso, ou o token recém-emitido foi recusado: retentar não muda permissão, e pode
            // bloquear a conta. Rejeição com o motivo da plataforma, que na dead-letter se perderia (ADR-0027).
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.Unauthorized when token.IsFresh:
                throw new DispatchRejectedException(
                    $"Configuração do conector: a plataforma negou acesso ao tenant '{settings.TenantId}' no ambiente '{settings.Environment}' "
                    + $"(HTTP {status}{(status == 401 ? ", com token recém-emitido" : string.Empty)}): {PlatformMessage.Extract(body, status)}");

            // Token do cache vencido ou revogado: descarta, e a próxima tentativa pede outro (retry nativo).
            case HttpStatusCode.Unauthorized:
                throw Unauthorized(token, settings);
        }

        // O resto (5xx, 429, 404) segue como exceção, para o retry nativo e a dead-letter (ADR-0004). Só o status.
        response.EnsureSuccessStatusCode();

        // 6. Recibo, com o que o destino não levou. Sucesso sem identificador não é reenviado: retentar mandaria de novo
        //    um documento talvez já aceito (ADR-0027).
        string externalId = SubmittedId(body)
            ?? throw new DispatchRejectedException(
                $"Conector: a plataforma respondeu HTTP {status} com sucesso, mas sem identificador reconhecível. O documento pode ter "
                + "sido aceito e não será reenviado automaticamente. Veja a resposta gravada.");

        return new IntegrationReceipt { ExternalId = externalId, Status = IntegrationStatus.Submitted, Omissions = mapping.Omissions };
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
    {
        AvalaraOutboundSettings settings = AvalaraOutboundSettings.Read(context.TenantId, await _profiles.GetAsync(context.TenantId, ct));
        var statusUri = new Uri(settings.BaseUri, $"{_options.DocumentsPath}/{Uri.EscapeDataString(externalId)}/status");

        using var request = new HttpRequestMessage(HttpMethod.Get, statusUri);
        AvalaraAccessToken token = await ApplyAuthAsync(request, settings, ct);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);

        // Toda resposta com corpo sobrescreve a foto da consulta; a sem corpo (204, 404 pendente) não.
        (string raw, int redactions) = await ReadRedactedAsync(response, token, ct);
        if (raw.Length > 0)
        {
            await PhotographAsync(context, TraceExchanges.Status, request, response, raw, redactions, token, ct);
        }

        // 401 na consulta: descarta o token e deixa para a próxima passada do poll (limite em MaxAttempts).
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw Unauthorized(token, settings);
        }

        // 204 (sem conteúdo) e 404 (identificador ainda desconhecido) = a plataforma não processou
        // o documento ainda → segue pendente, não é erro. A consulta se repete e, no limite de
        // tentativas, vira Unconfirmed. Assim um GUID problemático não trava o poll do lote.
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return new IntegrationResult { Status = IntegrationStatus.Submitted };
        }

        response.EnsureSuccessStatusCode();

        using JsonDocument body = ParseStatus(raw);
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

    // Bearer por requisição (thread-safe; não mexe no HttpClient compartilhado), com a credencial do tenant no ambiente
    // ativo. Só a composição sem autenticação, pedida explicitamente, devolve "sem token" (ADR-0027).
    private async Task<AvalaraAccessToken> ApplyAuthAsync(HttpRequestMessage request, AvalaraOutboundSettings settings, CancellationToken ct)
    {
        AvalaraAccessToken token = await _tokenProvider.GetTokenAsync(settings, ct);
        if (!token.IsNone)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }

        return token;
    }

    // O corpo cru, redigido antes de qualquer uso: nem a foto, nem o motivo, nem um log veem o token em uso.
    private static async Task<(string Body, int Redactions)> ReadRedactedAsync(HttpResponseMessage response, AvalaraAccessToken token, CancellationToken ct)
        => SensitiveText.Redact(await response.Content.ReadAsStringAsync(ct), [token.Value]);

    // A quarta foto: o envelope do D8, já redigido. Melhor esforço — a falha é logada sem o conteúdo, e o desfecho segue
    // como se tivesse dado certo, porque a requisição já saiu.
    private async Task PhotographAsync(
        DispatchContext context, string exchange, HttpRequestMessage request, HttpResponseMessage response, string body, int redactions,
        AvalaraAccessToken token, CancellationToken ct)
    {
        try
        {
            string envelope = PlatformResponseEnvelope.Build(exchange, request, response, body, redactions, [token.Value], _clock.GetUtcNow());
            await _trace.SaveResponseAsync(context.TenantId, context.NaturalKey, Destination, exchange, envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "A foto da resposta ({Exchange}) do documento {NaturalKey} do tenant {Tenant} não foi gravada ({Error}); o desfecho segue.",
                exchange, context.NaturalKey, context.TenantId, ex.GetType().Name);
        }
    }

    private HttpRequestException Unauthorized(AvalaraAccessToken token, AvalaraOutboundSettings settings)
    {
        _tokenProvider.Invalidate(token);
        return new HttpRequestException(
            $"A plataforma recusou o token em cache do tenant '{settings.TenantId}' no ambiente '{settings.Environment}' (HTTP 401); "
            + "o token foi descartado, e a próxima tentativa pede outro.", null, HttpStatusCode.Unauthorized);
    }

    // O identificador do envio: o "id" texto não vazio de um objeto JSON. Qualquer outra coisa é "sem identificador".
    internal static string? SubmittedId(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? StringProperty(doc.RootElement, "id") is { Length: > 0 } id ? id : null : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A consulta de status é lida como JSON cru: além do status, o motivo da recusa vem em formato ainda não gravado.
    private static JsonDocument ParseStatus(string raw)
    {
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
}
