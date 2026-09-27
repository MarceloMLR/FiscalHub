using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Infrastructure;
using FiscalHub.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Sonda do sandbox da plataforma de compliance (ADR-0027, design D12). É a ferramenta do teste manual e do experimento
// do campo omitido — não a esteira: manda variantes de payload que o mapper não produz, e grava a resposta como
// evidência. Reusa o que o hub usa: o perfil do tenant no banco de dev, o segredo no cofre (em dev, o emulador, com o
// valor gravado pela tela), o provider de token, a redação e o envelope da foto. Só LÊ: nunca grava no cofre nem no
// banco. Nunca imprime token nem segredo. Tudo o que grava em out/ já sai redigido, e out/ não entra no Git.

const string Usage = """
    Uso (da raiz do repositório):
      dotnet run --project tools/AvalaraSandboxProbe -- token --tenant tenant-a
      dotnet run --project tools/AvalaraSandboxProbe -- send  --tenant tenant-a --payload <avalara.json> --label <nome>
                  [--omit campo]... [--set campo=valor]... [--ref-suffix <s>] [--poll] [--poll-attempts 10] [--poll-seconds 5]
      dotnet run --project tools/AvalaraSandboxProbe -- get   --tenant tenant-a --id <id> --label <nome>

    Opções comuns: --host-settings <pasta do appsettings do Host> (padrão: src/FiscalHub.Host)
                   --out <pasta de saída> (padrão: tools/AvalaraSandboxProbe/out)
    """;

ProbeArgs? parsed = ProbeArgs.Parse(args);
if (parsed is null || parsed.Value("tenant") is not { } tenant)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

string root = RepoRoot();
string hostDir = parsed.Value("host-settings") ?? Path.Combine(root, "src", "FiscalHub.Host");
string outDir = parsed.Value("out") ?? Path.Combine(root, "tools", "AvalaraSandboxProbe", "out");

// A configuração do Host: o mesmo banco de dev, o mesmo cofre e as mesmas opções do adapter.
IConfiguration cfg = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(hostDir, "appsettings.json"), optional: false)
    .AddJsonFile(Path.Combine(hostDir, "appsettings.Development.json"), optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddSqlProcessingStore(cfg.GetConnectionString("Sql") ?? throw new InvalidOperationException("ConnectionStrings:Sql ausente na configuração do Host."));
services.AddKeyVaultSecretStore(cfg.GetSection("SecretStore").Get<KeyVaultSecretStoreSettings>() ?? new KeyVaultSecretStoreSettings());
await using ServiceProvider sp = services.BuildServiceProvider();

var options = new AvalaraOptions();
cfg.GetSection("Avalara").Bind(options);
var secrets = new ReadOnlySecretStore(sp.GetRequiredService<ISecretStore>());

TenantConnectorProfile? profile;
await using (AsyncServiceScope scope = sp.CreateAsyncScope())
{
    profile = await scope.ServiceProvider.GetRequiredService<IConnectorProfileStore>().GetAsync(tenant);
}

AvalaraOutboundSettings settings = AvalaraOutboundSettings.Read(tenant, profile);
var tokenExchange = new CapturingHandler(new HttpClientHandler());
var provider = new AvalaraTokenProvider(
    new HttpClient(tokenExchange), secrets, Options.Create(options), TimeProvider.System, NullLogger<AvalaraTokenProvider>.Instance);
using var http = new HttpClient();
Directory.CreateDirectory(outDir);

try
{
    return parsed.Command switch
    {
        "token" => await TokenAsync(),
        "send" => await SendAsync(),
        "get" => await GetAsync(),
        _ => Fail(Usage, 2),
    };
}
catch (DispatchRejectedException ex)
{
    // A configuração do tenant, a credencial ou a recusa da plataforma — o motivo já vem sem segredo.
    return Fail(ex.Reason, 1);
}

// ---- token: a verificação da premissa de autenticação (design D13) ----
async Task<int> TokenAsync()
{
    Console.WriteLine($"Tenant {tenant}, ambiente {settings.Environment}: pedido de token a {settings.TokenEndpoint(options.TokenPath)}");
    string? secret = await secrets.GetAsync(settings.Credential.SecretName);   // só para redigir o que for gravado

    AvalaraAccessToken? token = null;
    string? failure = null;
    try
    {
        token = await provider.GetTokenAsync(settings);
    }
    catch (DispatchRejectedException ex)
    {
        failure = ex.Reason;
    }
    catch (HttpRequestException ex)
    {
        failure = $"falha transitória do endpoint de token (HTTP {(int?)ex.StatusCode})";
    }

    if (tokenExchange.Last is { } exchange)
    {
        // A regra da troca de token: credenciais [redigido], e a sessão, a conta e o login [mascarado] — o token.json é
        // evidência para colar em PR.
        TokenExchangeRedaction redaction = TokenExchangeRedaction.For(exchange.Body, [secret, token?.Value]);
        (string body, int redactions) = redaction.Redact(exchange.Body);
        Save("token.json", PlatformResponseEnvelope.Build("token", exchange.Request, exchange.Response, body, redactions, redaction.Redact, DateTimeOffset.UtcNow));
        Console.WriteLine($"HTTP {(int)exchange.Response.StatusCode}; campos da resposta: {string.Join(", ", FieldNames(exchange.Body))}");
        if (ExpiresIn(exchange.Body) is { } expiresIn)
        {
            Console.WriteLine($"expires_in: {expiresIn}");
        }
        else
        {
            Console.WriteLine("expires_in: ausente (o token seria usado sem cache)");
        }
    }

    Console.WriteLine(token is not null ? "Token obtido: sim (o valor não é impresso)." : $"Token obtido: não. {failure}");
    return token is not null ? 0 : 1;
}

// ---- send: uma variante do payload de destino, com o mesmo token e o mesmo caminho do hub ----
async Task<int> SendAsync()
{
    string label = parsed.Required("label");
    JsonObject payload = JsonNode.Parse(File.ReadAllText(parsed.Required("payload"))) as JsonObject
        ?? throw new InvalidOperationException("O payload precisa ser um objeto JSON (o <destino>.json do zip do hub).");

    foreach (string field in parsed.Values("omit"))
    {
        Console.WriteLine(payload.Remove(field) ? $"omitido: {field}" : $"omitido: {field} (não estava no payload)");
    }

    foreach (string assignment in parsed.Values("set"))
    {
        int eq = assignment.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0)
        {
            return Fail($"--set espera campo=valor, e veio '{assignment}'.", 2);
        }

        payload[assignment[..eq]] = ParseValue(assignment[(eq + 1)..]);
        Console.WriteLine($"definido: {assignment}");
    }

    if (parsed.Value("ref-suffix") is { } suffix)
    {
        payload["codigoReferenciaIntegracao"] = $"{(string?)payload["codigoReferenciaIntegracao"]}#{suffix}";
    }

    Save($"{label}.payload.json", payload.ToJsonString(Json.Indented));

    AvalaraAccessToken token = await provider.GetTokenAsync(settings);
    using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUri, options.DocumentsPath))
    {
        Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

    using HttpResponseMessage response = await http.SendAsync(request);
    (string body, int redactions) = SensitiveText.Redact(await response.Content.ReadAsStringAsync(), [token.Value]);
    Save($"{label}.submit.json", PlatformResponseEnvelope.Build("submit", request, response, body, redactions, RedactWith(token), DateTimeOffset.UtcNow));

    int status = (int)response.StatusCode;
    string? id = AvalaraComplianceDispatcher.SubmittedId(body);
    Console.WriteLine($"envio: HTTP {status}{(id is null ? string.Empty : $", id {id}")}{(redactions > 0 ? $", {redactions} redação(ões)" : string.Empty)}");
    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"motivo: {PlatformMessage.Extract(body, status)}");
    }

    if (parsed.Has("poll") && id is not null)
    {
        await PollAsync(label, id, token);
    }

    return response.IsSuccessStatusCode ? 0 : 1;
}

async Task PollAsync(string label, string id, AvalaraAccessToken token)
{
    int attempts = int.Parse(parsed.Value("poll-attempts") ?? "10", CultureInfo.InvariantCulture);
    var interval = TimeSpan.FromSeconds(int.Parse(parsed.Value("poll-seconds") ?? "5", CultureInfo.InvariantCulture));

    for (int attempt = 1; attempt <= attempts; attempt++)
    {
        await Task.Delay(interval);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(settings.BaseUri, $"{options.DocumentsPath}/{Uri.EscapeDataString(id)}/status"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        using HttpResponseMessage response = await http.SendAsync(request);
        (string body, int redactions) = SensitiveText.Redact(await response.Content.ReadAsStringAsync(), [token.Value]);

        string? native = null;
        if (body.Length > 0)
        {
            Save($"{label}.status.json", PlatformResponseEnvelope.Build("status", request, response, body, redactions, RedactWith(token), DateTimeOffset.UtcNow));
            native = Field(body, "status");
        }

        Console.WriteLine($"consulta {attempt}/{attempts}: HTTP {(int)response.StatusCode}{(native is null ? string.Empty : $", status '{native}'")}");
        if (native is not null && native.Trim().ToLowerInvariant() is "carregado" or "erro")
        {
            return;
        }
    }
}

// ---- get: a leitura de volta, se a plataforma a oferecer ----
async Task<int> GetAsync()
{
    string label = parsed.Required("label");
    string id = parsed.Required("id");
    AvalaraAccessToken token = await provider.GetTokenAsync(settings);

    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(settings.BaseUri, $"{options.DocumentsPath}/{Uri.EscapeDataString(id)}"));
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    using HttpResponseMessage response = await http.SendAsync(request);
    (string body, int redactions) = SensitiveText.Redact(await response.Content.ReadAsStringAsync(), [token.Value]);
    Save($"{label}.readback.json", PlatformResponseEnvelope.Build("readback", request, response, body, redactions, RedactWith(token), DateTimeOffset.UtcNow));

    Console.WriteLine($"leitura de volta: HTTP {(int)response.StatusCode}");
    return response.IsSuccessStatusCode ? 0 : 1;
}

void Save(string file, string content)
{
    string path = Path.Combine(outDir, file);
    File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"gravado (redigido): {Path.GetRelativePath(root, path)}");
}

// A regra do envio: o token em uso, e o Bearer e os nomes sensíveis.
static Func<string, (string Text, int Redactions)> RedactWith(AvalaraAccessToken token) => value => SensitiveText.Redact(value, [token.Value]);

static int Fail(string message, int code)
{
    Console.Error.WriteLine(message);
    return code;
}

// Número, booleano, null, objeto ou texto entre aspas viram JSON; o resto, texto.
static JsonNode? ParseValue(string raw)
{
    try
    {
        return JsonNode.Parse(raw);
    }
    catch (JsonException)
    {
        return JsonValue.Create(raw);
    }
}

static IEnumerable<string> FieldNames(string body)
{
    try
    {
        return JsonNode.Parse(body) is JsonObject obj ? [.. obj.Select(p => p.Key)] : ["(corpo não é um objeto JSON)"];
    }
    catch (JsonException)
    {
        return ["(corpo não é JSON)"];
    }
}

static string? ExpiresIn(string body)
{
    try
    {
        return JsonNode.Parse(body) is JsonObject obj && obj["expires_in"] is { } value ? value.ToJsonString() : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

static string? Field(string body, string name)
{
    try
    {
        return JsonNode.Parse(body) is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

static string RepoRoot()
{
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "FiscalHub.slnx")))
        {
            return dir.FullName;
        }
    }

    return Directory.GetCurrentDirectory();
}

internal static class Json
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

/// <summary>Argumentos: o comando e as opções <c>--nome valor</c>; <c>--poll</c> é marcador; <c>--omit</c> e <c>--set</c> repetem.</summary>
internal sealed class ProbeArgs
{
    private static readonly HashSet<string> Flags = ["poll"];

    private readonly Dictionary<string, List<string>> _options = new(StringComparer.Ordinal);

    private ProbeArgs(string command) => Command = command;

    public string Command { get; }

    public static ProbeArgs? Parse(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var parsed = new ProbeArgs(args[0]);
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            string name = args[i][2..];
            string value = Flags.Contains(name) ? "true" : i + 1 < args.Length ? args[++i] : string.Empty;
            if (!parsed._options.TryGetValue(name, out List<string>? values))
            {
                parsed._options[name] = values = [];
            }

            values.Add(value);
        }

        return parsed;
    }

    public string? Value(string name) => _options.TryGetValue(name, out List<string>? values) ? values[^1] : null;

    public IReadOnlyList<string> Values(string name) => _options.TryGetValue(name, out List<string>? values) ? values : [];

    public bool Has(string name) => _options.ContainsKey(name);

    public string Required(string name)
        => Value(name) is { Length: > 0 } value ? value : throw new ArgumentException($"--{name} é obrigatório.");
}

/// <summary>A sonda só lê o cofre: qualquer escrita é defeito.</summary>
internal sealed class ReadOnlySecretStore(ISecretStore inner) : ISecretStore
{
    public Task<string?> GetAsync(string name, CancellationToken ct = default) => inner.GetAsync(name, ct);

    public Task SetAsync(string name, string value, CancellationToken ct = default)
        => throw new InvalidOperationException("A sonda do sandbox não grava no cofre.");

    public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default) => inner.DescribeAsync(name, ct);
}

/// <summary>
/// Guarda uma cópia da última troca com o endpoint de token: o método e a URL do pedido (sem cabeçalho e sem corpo) e a
/// resposta, para a sonda mostrar os campos e gravar o envelope redigido. O provider continua lendo a resposta original.
/// </summary>
internal sealed class CapturingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public (HttpRequestMessage Request, HttpResponseMessage Response, string Body)? Last { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response = await base.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);   // fica em buffer: o provider lê de novo

        var copy = new HttpResponseMessage(response.StatusCode) { Content = new StringContent(body) };
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        copy.Content.Headers.Clear();
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
        {
            copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        Last = (new HttpRequestMessage(request.Method, request.RequestUri), copy, body);
        return response;
    }
}
