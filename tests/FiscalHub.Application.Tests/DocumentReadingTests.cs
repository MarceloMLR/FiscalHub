using System.Text;
using System.Text.Json;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Support;
using FiscalHub.Application.Tracing;

namespace FiscalHub.Application.Tests;

/// <summary>
/// A leitura do desfecho (spec platform-response-trace, design D8 revisado da change establishment-and-readable-dashboard):
/// o que a primeira vista do detalhe usa, tirado das fotos da resposta no servidor, e nada mais delas. É aberta a
/// qualquer papel do tenant; as fotos cruas não.
/// </summary>
public class DocumentReadingTests
{
    private static readonly string SandboxRefusal =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sandbox", "recusa-no-envio.json"));

    [Fact]
    public void Sandbox_refusal_gives_the_six_fields_in_the_response_order_with_their_messages()
    {
        DocumentReading reading = DocumentReading.From([Photo("tenant-a/202609/brmf|X/avalara.response.submit.json", SandboxRefusal)]);

        Assert.Equal(
            ["operacao", "tipoPagamento", "parceiro.Codigo", "itens[0].Item.TipoItem", "itens[0].UnidadeMedida.Descricao", "itens[0].Item.UnidadeMedida.Descricao"],
            reading.Fields.Select(f => f.Path));
        Assert.Equal(["'Operacao' não pode ser nulo."], reading.Fields[0].Messages);
        Assert.Equal(["'Codigo' não pode ser nulo.", "'Codigo' deve ser informado."], reading.Fields[2].Messages);
        Assert.Empty(reading.Omissions);
    }

    [Fact]
    public void Reading_carries_nothing_else_from_the_photos()
    {
        DocumentReading reading = DocumentReading.From([Photo("avalara.response.submit.json", SandboxRefusal)]);

        string json = JsonSerializer.Serialize(reading, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(["fields", "omissions"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("traceId", json);
        Assert.DoesNotContain("api-gateway", json);   // a URL do envelope
        Assert.DoesNotContain("One or more validation errors occurred.", json);
    }

    [Fact]
    public void Status_photo_with_a_field_map_wins_over_the_submit_photo()
    {
        DocumentReading reading = DocumentReading.From(
        [
            Photo("avalara.response.submit.json", Envelope(body: """{"errors":{"doSubmit":["x"]}}""")),
            Photo("avalara.response.status.json", Envelope(body: """{"id":"g","status":"erro","errors":{"cfop":["CFOP 1556 incompatível"]}}""")),
        ]);

        Assert.Equal(["cfop"], reading.Fields.Select(f => f.Path));
    }

    [Fact]
    public void Without_a_map_in_the_status_photo_the_submit_photo_is_read()
    {
        DocumentReading reading = DocumentReading.From(
        [
            Photo("avalara.response.status.json", Envelope(body: """{"id":"g","status":"erro","mensagens":["x"]}""")),
            Photo("avalara.response.submit.json", Envelope(body: """{"errors":{"operacao":["'Operacao' não pode ser nulo."]}}""")),
        ]);

        Assert.Equal(["operacao"], reading.Fields.Select(f => f.Path));
    }

    [Fact]
    public void Omissions_come_from_the_submit_photo_request()
    {
        DocumentReading reading = DocumentReading.From(
            [Photo("avalara.response.submit.json", Envelope(body: """{"id":"g"}""", omissions: """["item 1: IcmsDiff não enviado (sem lugar no contrato)"]"""))]);

        Assert.Empty(reading.Fields);
        Assert.Equal(["item 1: IcmsDiff não enviado (sem lugar no contrato)"], reading.Omissions);
    }

    [Theory]
    [InlineData("""{"mensagens":["codigoEmpresa não cadastrado"]}""")]   // sem mapa
    [InlineData("\"Documento inválido\"")]                              // corpo em texto
    [InlineData("""{"errors":["lista, e não mapa"]}""")]
    public void Body_without_a_field_map_gives_an_empty_list(string body)
        => Assert.Empty(DocumentReading.From([Photo("avalara.response.submit.json", Envelope(body))]).Fields);

    [Fact]
    public void Photo_that_is_not_json_and_other_files_are_ignored()
    {
        DocumentReading reading = DocumentReading.From(
        [
            Photo("avalara.response.submit.json", "não é json"),
            Photo("source.json", """{"errors":{"naoConta":["x"]}}"""),
            Photo("avalara.json", """{"request":{"omissions":["naoConta"]}}"""),
        ]);

        Assert.Empty(reading.Fields);
        Assert.Empty(reading.Omissions);
    }

    [Fact]
    public void The_last_photo_of_a_kind_prevails_like_in_the_trace()
    {
        // Reprocesso em outro mês: a foto repetida vem por último na listagem e prevalece (o /trace faz igual).
        DocumentReading reading = DocumentReading.From(
        [
            Photo("tenant-a/202608/k/avalara.response.submit.json", Envelope("""{"errors":{"antigo":["x"]}}""")),
            Photo("tenant-a/202609/k/avalara.response.submit.json", Envelope("""{"errors":{"novo":["y"]}}""")),
        ]);

        Assert.Equal(["novo"], reading.Fields.Select(f => f.Path));
    }

    [Fact]
    public async Task Query_is_not_found_for_another_tenant_and_without_photos()
    {
        var other = new DocumentReadingQuery(new DocumentTraceQuery(new Reader([Photo("avalara.response.submit.json", SandboxRefusal)]), new Tenant("tenant-b")));
        var empty = new DocumentReadingQuery(new DocumentTraceQuery(new Reader([]), new Tenant("tenant-a")));
        var own = new DocumentReadingQuery(new DocumentTraceQuery(new Reader([Photo("avalara.response.submit.json", SandboxRefusal)]), new Tenant("tenant-a")));

        Assert.Null(await other.GetAsync("tenant-a", "k"));
        Assert.Null(await empty.GetAsync("tenant-a", "k"));
        Assert.Equal(6, (await own.GetAsync("tenant-a", "k"))!.Fields.Count);
    }

    private static TraceFile Photo(string name, string content) => new(name, Encoding.UTF8.GetBytes(content));

    // O envelope da quarta foto (ADR-0027 §8), com o corpo e as omissões do pedido.
    private static string Envelope(string body, string? omissions = null)
        => """{"exchange":"submit","request":{"method":"POST","url":"https://x/dfe" """
           + (omissions is null ? "" : ""","omissions":""" + omissions)
           + """},"response":{"status":400,"body":""" + body + """},"redactions":0}""";

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class Reader(IReadOnlyList<TraceFile> files) : INoteTraceReader
    {
        public Task<IReadOnlyList<TraceFile>> ReadAsync(string tenantId, string naturalKey, CancellationToken ct = default)
            => Task.FromResult(files);
    }
}
