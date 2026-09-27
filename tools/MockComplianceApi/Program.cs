using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

// Mock minimal-API que simula a plataforma de compliance (Avalara) para testes manuais e2e.
// Fluxo de duas fases (ADR-0003): POST devolve um GUID (aceito); GET status devolve o resultado.
// Store em memória — some quando o processo reinicia. Nunca usar em produção.
// Os formatos de recusa ({"mensagens":[...]}) são PRESUMIDOS até haver resposta real gravada (ADR-0026); o hub
// extrai o motivo de forma tolerante e não depende deles.
// Autentica como a plataforma (ADR-0027): POST /oauth/token (client_credentials com corpo JSON, a forma da coleção do
// Postman do cliente) emite um token, e /documents* exigem o Bearer emitido aqui. /admin/* e a inspeção do payload
// continuam abertos, porque são ferramenta de dev.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// guid -> (status nativo, motivo da recusa, corpo JSON recebido)
var documents = new ConcurrentDictionary<string, (string Status, string? Reason, string Body)>();

// Resultado padrão aplicado a novos documentos quando o envio não traz ?resultado. O host não passa
// query, então este toggle é o que decide — permite forçar os caminhos de recusa ao vivo.
var toggle = new ResultToggle();

// Tokens emitidos por este processo, e o toggle que força a recusa da credencial (o roteiro local do D7).
var tokens = new ConcurrentDictionary<string, byte>();
var tokenToggle = new TokenToggle();

// Qualquer client_id e client_secret não vazios servem: o mock confere a forma, não a credencial. O corpo é JSON, como
// na coleção do cliente; formulário é recusado, para que uma regressão do provider apareça no ponta a ponta. O
// disableTokenRefresh da coleção é aceito, e não exigido: não há documentação dele.
app.MapPost("/oauth/token", async (HttpRequest request) =>
{
    JsonObject? body = null;
    if (request.HasJsonContentType())
    {
        try
        {
            body = await JsonNode.ParseAsync(request.Body) as JsonObject;
        }
        catch (JsonException)
        {
            // corpo inválido: cai no 400 abaixo
        }
    }

    if (body is null)
    {
        return Results.Json(new { error = "invalid_request", error_description = "o pedido de token é JSON (mock)" }, statusCode: StatusCodes.Status400BadRequest);
    }

    string? Field(string name) => body[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    if (Field("grant_type") != "client_credentials")
    {
        return Results.Json(new { error = "unsupported_grant_type" }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (string.IsNullOrEmpty(Field("client_id")) || string.IsNullOrEmpty(Field("client_secret")) || tokenToggle.Refuse)
    {
        return Results.Json(new { error = "invalid_client", error_description = "credencial recusada pelo mock" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    tokens[token] = 0;
    return Results.Ok(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
});

// Toggle (dev): aceitar | recusar a credencial nos próximos pedidos de token.
app.MapPost("/admin/token/{value}", (string value) =>
{
    tokenToggle.Refuse = value.Trim().Equals("recusar", StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new { token = tokenToggle.Refuse ? "recusar" : "aceitar" });
});

// Sem o Bearer emitido aqui, /documents* respondem 401 — o mock nunca aceita envio sem autenticação.
bool Authorized(HttpRequest request)
    => request.Headers.Authorization.ToString() is { } header
        && header.StartsWith("Bearer ", StringComparison.Ordinal)
        && tokens.ContainsKey(header["Bearer ".Length..]);

IResult Unauthenticated() => Results.Json(new { mensagens = new[] { "token ausente ou inválido (mock)" } }, statusCode: StatusCodes.Status401Unauthorized);

// Fase 1 — recebe o "god json" e devolve um identificador externo (GUID).
// ?resultado=carregado|erro|rejeitar sobrepõe o toggle para exercitar cada ramo.
app.MapPost("/documents", async (HttpRequest request, string? resultado) =>
{
    if (!Authorized(request))
    {
        return Unauthenticated();
    }

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var result = ResultToggle.Normalize(resultado) ?? toggle.Value;

    // Recusa síncrona: a plataforma não aceita o documento (o hub registra o motivo, sem retentativa).
    if (result == "rejeitar")
    {
        return Results.BadRequest(new { mensagens = new[] { toggle.Reason } });
    }

    var id = Guid.NewGuid().ToString();
    documents[id] = (result, result == "erro" ? toggle.Reason : null, body);
    return Results.Ok(new { id });
});

// Fase 2 — consulta o status final pelo GUID. No "erro", o motivo vem em "mensagens".
app.MapGet("/documents/{id}/status", (HttpRequest request, string id) =>
    !Authorized(request)
        ? Unauthenticated()
        : documents.TryGetValue(id, out var doc)
            ? doc.Reason is null
                ? Results.Ok(new { id, status = doc.Status })
                : Results.Ok(new { id, status = doc.Status, mensagens = new[] { doc.Reason } })
            : Results.NotFound(new { id, status = "desconhecido" }));

// Inspeção: devolve o JSON exato que o hub enviou (para ver o payload gerado). Aberta: é ferramenta de dev.
app.MapGet("/documents/{id}", (string id) =>
    documents.TryGetValue(id, out var doc)
        ? Results.Content(doc.Body, "application/json")
        : Results.NotFound());

// Toggle (dev): força o resultado padrão dos próximos documentos (carregado | erro | rejeitar), com motivo
// opcional (?motivo=...), pra exercitar erro, recusa e reprocesso ao vivo.
app.MapPost("/admin/result/{value}", (string value, string? motivo) =>
{
    toggle.Value = ResultToggle.Normalize(value) ?? "carregado";
    toggle.Reason = string.IsNullOrWhiteSpace(motivo) ? ResultToggle.DefaultReason : motivo;
    return Results.Ok(new { defaultResult = toggle.Value, reason = toggle.Reason });
});

app.MapGet("/admin/result", () => Results.Ok(new { defaultResult = toggle.Value, reason = toggle.Reason }));

app.Run();

// Estado do toggle de resultado (dev). Só o mock usa; não vai pra produção.
internal sealed class ResultToggle
{
    public const string DefaultReason = "Documento recusado pelo mock (simulação de recusa da plataforma).";

    public string Value { get; set; } = "carregado";

    public string Reason { get; set; } = DefaultReason;

    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "carregado" => "carregado",
        "erro" => "erro",
        "rejeitar" => "rejeitar",
        _ => null,
    };
}

// Recusa forçada da credencial (dev).
internal sealed class TokenToggle
{
    public bool Refuse { get; set; }
}

// Exposto para o teste ponta a ponta subir o mock em memória (WebApplicationFactory<Program>).
public partial class Program;
