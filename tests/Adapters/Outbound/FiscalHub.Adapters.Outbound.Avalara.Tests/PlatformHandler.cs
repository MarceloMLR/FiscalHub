using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Uma plataforma falsa com a listagem de estabelecimentos e o envio (change platform-establishment-resolution, tarefa
/// 2.1), sem lib de mock. Responde por caminho e pela query: as empresas num array puro, os contribuintes de cada
/// <c>empresaId</c> em <c>{"value": [...]}</c>, paginados de verdade pelo <c>$top</c> e pelo <c>$skip</c>. Conta as
/// requisições por caminho e guarda as queries — o resultado certo com N listagens é o defeito que os testes procuram.
/// </summary>
internal sealed class PlatformHandler : HttpMessageHandler
{
    public const string CompaniesPath = "/taxcompliance/v2/empresa";
    public const string TaxpayersPath = "/taxcompliance/v2/contribuinte";

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing");

    private readonly object _gate = new();
    private int _submitted;

    /// <summary>As empresas, na ordem da plataforma.</summary>
    public List<JsonObject> Companies { get; } = [];

    /// <summary>Os contribuintes de cada empresa, pelo <c>empresaId</c> como texto.</summary>
    public Dictionary<string, List<JsonObject>> Taxpayers { get; } = [];

    /// <summary>O limite de página do próprio servidor: devolve no máximo isto, mesmo com um <c>$top</c> maior.</summary>
    public int? ServerPageLimit { get; set; }

    /// <summary>Ignora o <c>$skip</c>: toda página é a primeira.</summary>
    public bool IgnoreSkip { get; set; }

    /// <summary>Uma resposta no lugar da normal, para provocar as falhas: devolve null para seguir normal.</summary>
    public Func<Request, HttpResponseMessage?>? Override { get; set; }

    public List<Request> Requests { get; } = [];

    public List<string> SubmittedBodies { get; } = [];

    /// <summary>A plataforma das fixtures: a empresa 012 com três estabelecimentos, a Comércio vazia e a 009.</summary>
    public static PlatformHandler FromFixtures()
    {
        var handler = new PlatformHandler();
        handler.Companies.AddRange(JsonNode.Parse(Fixture("empresas.json"))!.AsArray().Select(n => n!.AsObject()));
        foreach (string id in new[] { "8120", "8121", "8122" })
        {
            handler.Taxpayers[id] = [.. JsonNode.Parse(Fixture($"contribuintes-{id}.json"))!["value"]!.AsArray().Select(n => n!.AsObject())];
        }

        return handler;
    }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(FixtureDir, name));

    /// <summary>Uma empresa com os contribuintes dela, cada um um (cnpj, codigo, contribuinteId).</summary>
    public PlatformHandler WithCompany(object empresaId, string? codigoCia, string? descricao, params (string? Cnpj, string? Codigo, object? Id)[] taxpayers)
    {
        var company = new JsonObject { ["empresaId"] = Value(empresaId), ["codigoCIA"] = codigoCia, ["descricao"] = descricao };
        Companies.Add(company);
        Taxpayers[empresaId.ToString()!] = [.. taxpayers.Select(t => new JsonObject
        {
            ["contribuinteId"] = Value(t.Id),
            ["codigo"] = t.Codigo,
            ["cnpj"] = t.Cnpj,
        })];
        return this;
    }

    public int Count(string path)
    {
        lock (_gate)
        {
            return Requests.Count(r => r.Path == path);
        }
    }

    public List<Request> To(string path)
    {
        lock (_gate)
        {
            return [.. Requests.Where(r => r.Path == path)];
        }
    }

    // Número ou texto, como a plataforma devolver: o identificador não é tipado pelo hub.
    private static JsonNode? Value(object? value) => value switch
    {
        null => null,
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        string text => JsonValue.Create(text),
        _ => throw new ArgumentException($"Identificador de tipo {value.GetType().Name} não suportado no teste."),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Uri uri = request.RequestUri!;
        var recorded = new Request(request.Method, uri, HttpUtility.ParseQueryString(uri.Query), request.Headers.Authorization?.ToString(), body);
        lock (_gate)
        {
            Requests.Add(recorded);
        }

        if (Override?.Invoke(recorded) is { } overridden)
        {
            return overridden;
        }

        if (recorded.Method == HttpMethod.Post)
        {
            int n = Interlocked.Increment(ref _submitted);
            lock (_gate)
            {
                SubmittedBodies.Add(body ?? string.Empty);
            }

            return Json(HttpStatusCode.OK, $$"""{"id":"ext-{{n}}"}""");
        }

        return recorded.Path switch
        {
            CompaniesPath => Json(HttpStatusCode.OK, new JsonArray([.. Page(Companies, recorded).Select(c => c.DeepClone())]).ToJsonString()),
            TaxpayersPath => Json(HttpStatusCode.OK, new JsonObject
            {
                ["value"] = new JsonArray([.. Page(Taxpayers.GetValueOrDefault(recorded.Query["empresaId"] ?? string.Empty) ?? [], recorded)
                    .Select(t => t.DeepClone())]),
            }.ToJsonString()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private IEnumerable<JsonObject> Page(List<JsonObject> items, Request request)
    {
        int top = int.TryParse(request.Query["$top"], out int t) ? t : items.Count;
        int skip = IgnoreSkip ? 0 : int.TryParse(request.Query["$skip"], out int s) ? s : 0;
        int size = ServerPageLimit is { } limit ? Math.Min(top, limit) : top;
        return items.Skip(skip).Take(size);
    }

    /// <summary>Uma requisição recebida: o método, o caminho, a query e o cabeçalho de autorização.</summary>
    internal sealed record Request(HttpMethod Method, Uri Uri, System.Collections.Specialized.NameValueCollection Query, string? Authorization, string? Body)
    {
        public string Path => Uri.AbsolutePath;

        public int Skip => int.Parse(Query["$skip"] ?? "-1");
    }
}
