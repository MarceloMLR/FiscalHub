using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// O corpo da foto sem o ruído do ProblemDetails (spec platform-response-trace, design D10 da change
/// establishment-and-readable-dashboard): com o mapa <c>errors</c>, saem o <c>type</c>, o <c>title</c> e o <c>status</c>
/// repetido; o resto fica. Sem o mapa, o corpo fica como veio.
/// </summary>
public class ProblemDetailsNoiseTests
{
    [Fact]
    public void Recorded_sandbox_refusal_loses_type_title_and_repeated_status_and_keeps_errors_and_trace_id()
    {
        (int status, string body) = SandboxFixtureTests.RecordedSubmit("recusa-no-envio.json");

        JsonObject photo = JsonNode.Parse(ProblemDetailsNoise.Strip(body, status))!.AsObject();

        Assert.False(photo.ContainsKey("type"));
        Assert.False(photo.ContainsKey("title"));
        Assert.False(photo.ContainsKey("status"));
        Assert.Equal("00-bb582c93ed4d2544429b0af1cbf7fd05-c04a22f297974e99-00", (string?)photo["traceId"]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(body)!["errors"], photo["errors"]));
    }

    [Fact]
    public void Status_different_from_the_http_status_stays()
    {
        JsonObject photo = JsonNode.Parse(ProblemDetailsNoise.Strip("""{"title":"t","status":422,"errors":{"a":["x"]}}""", 400))!.AsObject();

        Assert.Equal(422, (int?)photo["status"]);
        Assert.False(photo.ContainsKey("title"));
    }

    [Theory]
    [InlineData("""{"id":"3f2c","status":"erro","mensagens":["x"]}""")]   // resposta de status: o status nativo nunca sai
    [InlineData("""{"title":"Documento duplicado","status":400}""")]      // ProblemDetails sem mapa: o title pode ser a mensagem
    [InlineData("""{"errors":["lista, e não mapa"],"title":"t"}""")]
    public void Body_without_a_field_map_stays_as_it_came(string body)
        => Assert.Equal(body, ProblemDetailsNoise.Strip(body, 400));

    [Theory]
    [InlineData("Documento inválido")]
    [InlineData("")]
    public void Body_that_is_not_json_stays_as_it_came(string body)
        => Assert.Equal(body, ProblemDetailsNoise.Strip(body, 400));
}
