using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

// Mock minimal-API que simula a plataforma de compliance (Avalara) para testes manuais e2e.
// Fluxo de duas fases (ADR-0003): POST devolve um GUID (aceito); GET status devolve o resultado.
// Store em memória — some quando o processo reinicia. Nunca usar em produção.
// A recusa no envio imita a do sandbox (2026-09-27, Fixtures/sandbox/recusa-no-envio.json): HTTP 400 com o ProblemDetails
// (type, title, status, traceId e o mapa errors por campo). O erro da consulta de status ({"mensagens":[...]}) continua
// PRESUMIDO, porque a consulta não foi exercitada no sandbox, e não se fabrica forma.
// Autentica como a plataforma (ADR-0027): POST /oauth/token (client_credentials com corpo JSON, a forma da coleção do
// Postman do cliente) emite um token, e /documents* exigem o Bearer emitido aqui. /admin/* e a inspeção do payload
// continuam abertos, porque são ferramenta de dev.
// Lista os estabelecimentos como a plataforma (changes platform-establishment-resolution e platform-listing-shape):
// GET /taxcompliance/v2/empresa e /taxcompliance/v2/contribuinte?empresaId=, com $top, $skip, $orderby e $select. A forma
// depende da query, como no sandbox: com opções OData, {"value": [...]}, inclusive na página vazia; sem nenhuma, o array
// puro. A conta é de mentira, e os modos ficam em /admin/contribuintes.

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
// A recusa tem a forma do sandbox (2026-09-27): HTTP 400 com {"error": "<texto livre>"}, sem error_description, e o texto
// não é o código do OAuth — o segredo errado volta como "client_id invalid". Cada diferença entre o mock e a plataforma é
// um ensaio que passa e um envio real que falha.
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
        return Refused("request body invalid");
    }

    string? Field(string name) => body[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    if (Field("grant_type") != "client_credentials")
    {
        return Refused("grant_type invalid");
    }

    if (string.IsNullOrEmpty(Field("client_id")) || string.IsNullOrEmpty(Field("client_secret")) || tokenToggle.Refuse)
    {
        return Refused("client_id invalid");
    }

    // A forma da resposta real do sandbox (2026-09-27), com valores de mentira: o ensaio da sonda contra o mock passa pela
    // mesma redação (as credenciais [redigido], a sessão, a conta e o login [mascarado]).
    string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    tokens[token] = 0;
    return Results.Ok(new
    {
        access_token = token,
        token_type = "bearer",
        expires_in = 86400,
        refresh_token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
        sessionId = Guid.NewGuid().ToString(),
        userId = 900001,
        subId = "mock-sub-0001",
        appId = "mock-app-0001",
        login = "integracao@empresa-do-mock",
    });
});

static IResult Refused(string error) => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

// Toggle (dev): aceitar | recusar a credencial nos próximos pedidos de token.
app.MapPost("/admin/token/{value}", (string value) =>
{
    tokenToggle.Refuse = value.Trim().Equals("recusar", StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new { token = tokenToggle.Refuse ? "recusar" : "aceitar" });
});

// Os caminhos de envio: o do sandbox (Avalara:DocumentsPath do appsettings.Development.json, da URL de envio do cliente)
// e o /documents de antes. Os dois atendem o mesmo store, e o status e a inspeção seguem o mesmo prefixo.
string[] documentPaths = ["/taxcompliance/v2/fiscal/dfe", "/documents"];

// Sem o Bearer emitido aqui, os caminhos de envio e de status respondem 401 — o mock nunca aceita envio sem autenticação.
bool Authorized(HttpRequest request)
    => request.Headers.Authorization.ToString() is { } header
        && header.StartsWith("Bearer ", StringComparison.Ordinal)
        && tokens.ContainsKey(header["Bearer ".Length..]);

// O ProblemDetails de validação do sandbox, com o motivo num campo do mapa errors.
static object SandboxRefusal(string reason) => new Dictionary<string, object>
{
    ["errors"] = new Dictionary<string, string[]> { ["documento"] = [reason] },
    ["type"] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    ["title"] = "One or more validation errors occurred.",
    ["status"] = 400,
    ["traceId"] = $"00-{Guid.NewGuid():N}-{Guid.NewGuid():N}"[..52] + "-00",
};

IResult Unauthenticated() => Results.Json(new { mensagens = new[] { "token ausente ou inválido (mock)" } }, statusCode: StatusCodes.Status401Unauthorized);

foreach (string path in documentPaths)
{
    // Fase 1 — recebe o "god json" e devolve um identificador externo (GUID).
    // ?resultado=carregado|erro|rejeitar sobrepõe o toggle para exercitar cada ramo.
    app.MapPost(path, async (HttpRequest request, string? resultado) =>
    {
        if (!Authorized(request))
        {
            return Unauthenticated();
        }

        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();

        var result = ResultToggle.Normalize(resultado) ?? toggle.Value;

        // Recusa síncrona: a plataforma não aceita o documento (o hub registra o motivo, sem retentativa). A forma é a do
        // sandbox; o motivo do toggle vai num campo do mapa errors.
        if (result == "rejeitar")
        {
            return Results.Json(SandboxRefusal(toggle.Reason), statusCode: StatusCodes.Status400BadRequest);
        }

        var id = Guid.NewGuid().ToString();
        documents[id] = (result, result == "erro" ? toggle.Reason : null, body);
        return Results.Ok(new { id });
    });

    // Fase 2 — consulta o status final pelo GUID. No "erro", o motivo vem em "mensagens".
    app.MapGet($"{path}/{{id}}/status", (HttpRequest request, string id) =>
        !Authorized(request)
            ? Unauthenticated()
            : documents.TryGetValue(id, out var doc)
                ? doc.Reason is null
                    ? Results.Ok(new { id, status = doc.Status })
                    : Results.Ok(new { id, status = doc.Status, mensagens = new[] { doc.Reason } })
                : Results.NotFound(new { id, status = "desconhecido" }));

    // Inspeção: devolve o JSON exato que o hub enviou (para ver o payload gerado). Aberta: é ferramenta de dev.
    app.MapGet($"{path}/{{id}}", (string id) =>
        documents.TryGetValue(id, out var doc)
            ? Results.Content(doc.Body, "application/json")
            : Results.NotFound());
}

// A listagem de estabelecimentos (change platform-establishment-resolution), como no sandbox e no Swagger: o empresaId
// obrigatório nos contribuintes, e a paginação pelo cliente com $top e $skip, sem nextLink. O $orderby ordena pelo campo
// pedido; sem ele, a ordem muda a cada pedido, para que um hub que esquecesse a ordem falhasse aqui, e não por sorte. O
// $select devolve só os campos pedidos. Exigem o Bearer, como o envio.
//
// A forma, de qual chamada veio cada uma (change platform-listing-shape):
// - o /empresa sem opções de query devolve o array puro (2026-10-02);
// - o /empresa com $top e $orderby, com e sem $skip, devolve {"value": [...]}, e a página vazia vem como {"value": []}
//   (chamadas diretas de 2026-10-06). É a forma que o hub recebe, porque sempre chama com a query. Aqui, qualquer parâmetro
//   que começa com $ liga o envelope: qual opção sozinha o liga no sandbox não foi isolado, e o hub aceita as duas formas;
// - o /contribuinte devolve {"value": [...]}. Ele sempre foi chamado com query, e a forma dele sem opções nunca foi
//   observada, então continua sempre em envelope: não se fabrica forma.
var directory = new PlatformDirectory();

app.MapGet("/taxcompliance/v2/empresa", (HttpRequest request) =>
    !Authorized(request)
        ? Unauthenticated()
        : request.Query.Keys.Any(k => k.StartsWith('$'))
            ? Results.Json(new { value = directory.Companies(request.Query) })
            : Results.Json(directory.Companies(request.Query)));

app.MapGet("/taxcompliance/v2/contribuinte", (HttpRequest request, string? empresaId) =>
    !Authorized(request)
        ? Unauthenticated()
        : string.IsNullOrWhiteSpace(empresaId)
            ? Results.Json(new { mensagens = new[] { "empresaId é obrigatório (mock)" } }, statusCode: StatusCodes.Status400BadRequest)
            : Results.Json(new { value = directory.Taxpayers(empresaId, request.Query) }));

// Os modos da listagem (dev): a duplicidade (um CNPJ que já está, noutra empresa), o CNPJ sem cadastro, o limite de página
// do próprio servidor, e os contadores de requisições, que contam cada página.
app.MapPost("/admin/contribuintes/adicionar", (string cnpj, string? empresa, string? codigo) =>
    directory.Add(cnpj, empresa ?? "009", codigo ?? "001") is { } added
        ? Results.Ok(added)
        : Results.NotFound(new { mensagem = $"empresa '{empresa}' não existe no mock" }));

app.MapPost("/admin/contribuintes/remover", (string cnpj) => Results.Ok(new { removidos = directory.Remove(cnpj) }));

app.MapPost("/admin/contribuintes/restaurar", () =>
{
    directory.Restore();
    return Results.Ok(directory.Snapshot());
});

app.MapPost("/admin/listagem/limite", (int? itens) =>
{
    directory.PageLimit = itens is > 0 ? itens : null;
    return Results.Ok(new { limite = directory.PageLimit });
});

app.MapGet("/admin/contribuintes", () => Results.Ok(directory.Snapshot()));

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

// A conta de mentira da listagem (dev): uma empresa com os quatro estabelecimentos da brmf e o CNPJ dos XMLs de exemplo, e
// duas empresas de teste ao lado, como no sandbox. Tudo é inventado, e nada vem da conta de sandbox: os CNPJs são os da
// Contoso no D365 de dev, de propósito, para as notas gravadas resolverem aqui. O codigoCIA não acompanha a ordem do
// empresaId (8120 é "012", 8122 é "009"), como no sandbox. Os códigos dos contribuintes NÃO seguem a ordem do CNPJ (a
// Matriz, 0001, não é "001"): um código derivado da ordem falharia aqui, em vez de passar por coincidência.
internal sealed class PlatformDirectory
{
    private readonly object _gate = new();
    private List<Company> _companies = Initial();
    private int _nextId = 90001;

    public int? PageLimit { get; set; }

    public int CompanyRequests { get; private set; }

    public int TaxpayerRequests { get; private set; }

    public List<Dictionary<string, object?>> Companies(IQueryCollection query)
    {
        lock (_gate)
        {
            CompanyRequests++;
            IEnumerable<Dictionary<string, object?>> rows = _companies.Select(c => new Dictionary<string, object?>
            {
                ["empresaId"] = c.EmpresaId,
                ["codigoCIA"] = c.CodigoCia,
                ["descricao"] = c.Descricao,
                ["idPortalCompany"] = c.IdPortalCompany,
            });
            return Page(rows, query);
        }
    }

    public List<Dictionary<string, object?>> Taxpayers(string empresaId, IQueryCollection query)
    {
        lock (_gate)
        {
            TaxpayerRequests++;
            Company? company = _companies.FirstOrDefault(c => c.EmpresaId.ToString() == empresaId);
            IEnumerable<Dictionary<string, object?>> rows = (company?.Taxpayers ?? []).Select(t => new Dictionary<string, object?>
            {
                ["contribuinteId"] = t.ContribuinteId,
                ["empresaId"] = company!.EmpresaId,
                ["codigo"] = t.Codigo,
                ["cnpj"] = t.Cnpj,
                ["razao"] = t.Razao,
            });
            return Page(rows, query);
        }
    }

    public object? Add(string cnpj, string codigoCia, string codigo)
    {
        lock (_gate)
        {
            Company? company = _companies.FirstOrDefault(c => c.CodigoCia == codigoCia);
            if (company is null)
            {
                return null;
            }

            var taxpayer = new Taxpayer(_nextId++, codigo, cnpj, $"Contribuinte adicionado ({codigoCia})");
            company.Taxpayers.Add(taxpayer);
            return new { empresa = company.CodigoCia, taxpayer.ContribuinteId, taxpayer.Codigo, taxpayer.Cnpj };
        }
    }

    public int Remove(string cnpj)
    {
        lock (_gate)
        {
            return _companies.Sum(c => c.Taxpayers.RemoveAll(t => t.Cnpj == cnpj));
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            _companies = Initial();
            PageLimit = null;
            CompanyRequests = 0;
            TaxpayerRequests = 0;
        }
    }

    public object Snapshot()
    {
        lock (_gate)
        {
            return new
            {
                empresas = _companies.Select(c => new { c.EmpresaId, c.CodigoCia, c.Descricao, contribuintes = c.Taxpayers }),
                limite = PageLimit,
                requisicoes = new { empresas = CompanyRequests, contribuintes = TaxpayerRequests },
            };
        }
    }

    // $orderby pelo campo pedido (sem ele, uma ordem que muda a cada pedido), $skip, $top com o limite de página do próprio
    // servidor, e $select.
    private List<Dictionary<string, object?>> Page(IEnumerable<Dictionary<string, object?>> rows, IQueryCollection query)
    {
        string? orderBy = query["$orderby"].FirstOrDefault()?.Trim();
        List<Dictionary<string, object?>> ordered = string.IsNullOrEmpty(orderBy)
            ? [.. rows.OrderBy(_ => Random.Shared.Next())]
            : [.. rows.OrderBy(r => r.GetValueOrDefault(orderBy) is int number ? number.ToString("D12") : r.GetValueOrDefault(orderBy)?.ToString(), StringComparer.Ordinal)];

        int skip = int.TryParse(query["$skip"], out int s) && s > 0 ? s : 0;
        int top = int.TryParse(query["$top"], out int t) && t > 0 ? t : ordered.Count;
        int size = PageLimit is { } limit ? Math.Min(top, limit) : top;

        string[]? select = query["$select"].FirstOrDefault()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. ordered.Skip(skip).Take(size).Select(r => select is null ? r : r.Where(kv => select.Contains(kv.Key)).ToDictionary())];
    }

    private static List<Company> Initial() =>
    [
        new(8120, "012", "METALURGICA EXEMPLO (mock)", "00000000-0000-4000-8000-000000008120",
        [
            new(2000010001, "010", "44278225000180", "CONTOSO MATRIZ (mock)"),
            new(2000010002, "007", "44278225000260", "CONTOSO SP-01 (mock)"),
            new(2000010003, "021", "44278225000341", "CONTOSO SAL-01 (mock)"),
            new(2000010004, "003", "44278225003448", "CONTOSO RJ-01 (mock)"),
            new(2000010005, "015", "12345678000190", "EMITENTE DOS XMLS DE EXEMPLO (mock)"),
        ]),
        new(8121, "Comércio", "Comércio de exemplo (mock)", "00000000-0000-4000-8000-000000008121",
        [
            new(20001, "001", "11222333000181", "COMÉRCIO (mock)"),
        ]),
        new(8122, "009", "LABORATORIO (mock)", "00000000-0000-4000-8000-000000008122",
        [
            new(30001, "001", "99888777000166", "LABORATORIO (mock)"),
        ]),
    ];

    private sealed record Company(int EmpresaId, string CodigoCia, string Descricao, string IdPortalCompany, List<Taxpayer> Taxpayers);

    internal sealed record Taxpayer(int ContribuinteId, string Codigo, string Cnpj, string Razao);
}

// Exposto para o teste ponta a ponta subir o mock em memória (WebApplicationFactory<Program>).
public partial class Program;
