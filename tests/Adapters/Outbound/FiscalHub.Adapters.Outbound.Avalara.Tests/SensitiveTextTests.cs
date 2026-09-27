using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica a redação (ADR-0027): um ponto só, antes da foto, do motivo e de qualquer log. Por valor (o token e o
/// segredo em uso), por padrão (<c>Bearer &lt;valor&gt;</c>) e, quando é JSON, por nome de propriedade — com a contagem
/// do que foi redigido.
/// </summary>
public class SensitiveTextTests
{
    [Fact]
    public void Known_values_are_redacted_wherever_they_appear()
    {
        (string text, int count) = SensitiveText.Redact("token tok-123 e segredo s3cr3t, de novo tok-123", ["tok-123", "s3cr3t"]);

        Assert.Equal("token [redigido] e segredo [redigido], de novo [redigido]", text);
        Assert.Equal(3, count);
    }

    [Theory]
    [InlineData("Falhou com Authorization: Bearer abc.def-ghi", "Falhou com Authorization: Bearer [redigido]")]
    [InlineData("""{"eco":"bearer abc.def"}""", """{"eco":"bearer [redigido]"}""")]
    public void Bearer_values_are_redacted_in_text_and_in_json(string raw, string expected)
    {
        (string text, int count) = SensitiveText.Redact(raw, []);

        Assert.Equal(expected, text);
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("Authorization")]
    [InlineData("token")]
    [InlineData("refresh-token")]
    [InlineData("ID_TOKEN")]
    [InlineData("client_secret")]
    [InlineData("secret")]
    [InlineData("password")]
    [InlineData("senha")]
    [InlineData("apiKey")]
    public void Sensitive_property_names_are_redacted_at_any_level(string name)
    {
        string raw = $$$"""{"mensagens":["x"],"fundo":{"lista":[{"{{{name}}}":"abc"}]},"{{{name}}}":{"qualquer":"coisa"}}""";

        (string text, int count) = SensitiveText.Redact(raw, []);

        JsonNode root = JsonNode.Parse(text)!;
        Assert.Equal("[redigido]", (string?)root[name]);
        Assert.Equal("[redigido]", (string?)root["fundo"]!["lista"]![0]![name]);
        Assert.Equal("x", (string?)root["mensagens"]![0]);
        Assert.DoesNotContain("abc", text);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Count_adds_the_three_passes_without_counting_twice()
    {
        const string raw = """{"access_token":"tok-123","eco":"Bearer tok-123","outro":"Bearer xyz"}""";

        (string text, int count) = SensitiveText.Redact(raw, ["tok-123"]);

        JsonNode root = JsonNode.Parse(text)!;
        Assert.Equal("[redigido]", (string?)root["access_token"]);
        Assert.Equal("Bearer [redigido]", (string?)root["eco"]);
        Assert.Equal("Bearer [redigido]", (string?)root["outro"]);
        Assert.Equal(3, count);   // o valor duas vezes e o Bearer desconhecido uma; o nome já estava redigido
    }

    [Theory]
    [InlineData("""{"mensagens":["codigoEmpresa não cadastrado"],"id":"abc"}""")]
    [InlineData("Documento inválido")]
    [InlineData("")]
    public void Text_without_anything_sensitive_stays_the_same(string raw)
    {
        (string text, int count) = SensitiveText.Redact(raw, ["tok-123", "", null]);

        Assert.Equal(raw, text);
        Assert.Equal(0, count);
    }
}
