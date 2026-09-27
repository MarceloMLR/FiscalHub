using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica a redação da troca com o endpoint de token (ADR-0027), onde ela é gravada ou ecoada: o motivo da recusa
/// da credencial e a evidência da sonda (<c>out/token.json</c>), que existe para ser colada em PR. As credenciais saem
/// como <c>[redigido]</c>, e os identificadores da sessão e da conta, como <c>[mascarado]</c> — por nome e por valor.
/// O que a evidência precisa mostrar (<c>token_type</c>, <c>expires_in</c>) fica.
/// </summary>
public class TokenExchangeRedactionTests
{
    private const string Secret = "segredo-do-cliente";
    private const string AccessToken = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhYmMifQ.assinatura-do-token";
    private const string RefreshToken = "rt-3f9c1a7e5b2d4c6f8a0b";
    private const string SessionId = "5b1f3c2a-8d4e-4f6a-9b7c-1a2b3c4d5e6f";
    private const string SubId = "sub-7788990011";
    private const string AppId = "app-4455667788";
    private const string Login = "integracao@contoso-comercio-ltda";
    private const long UserId = 90817263;

    // A forma da resposta real do sandbox (2026-09-27), com valores de mentira.
    private static readonly string Response = $$"""
        {"access_token":"{{AccessToken}}","token_type":"bearer","expires_in":86400,"refresh_token":"{{RefreshToken}}",
         "sessionId":"{{SessionId}}","userId":{{UserId}},"subId":"{{SubId}}","appId":"{{AppId}}","login":"{{Login}}"}
        """;

    private static readonly string[] Values = [Secret, AccessToken, RefreshToken, SessionId, SubId, AppId, Login, UserId.ToString()];

    [Fact]
    public void No_value_of_the_real_response_shape_is_left_in_clear()
    {
        (string text, int count) = TokenExchangeRedaction.For(Response, [Secret]).Redact(Response);

        Assert.All(Values, v => Assert.DoesNotContain(v, text));
        JsonNode root = JsonNode.Parse(text)!;
        Assert.Equal("[redigido]", (string?)root["access_token"]);
        Assert.Equal("[redigido]", (string?)root["refresh_token"]);
        foreach (string id in new[] { "sessionId", "userId", "subId", "appId", "login" })
        {
            Assert.Equal("[mascarado]", (string?)root[id]);
        }

        Assert.Equal("bearer", (string?)root["token_type"]);    // o que a evidência precisa mostrar fica
        Assert.Equal(86400, (int?)root["expires_in"]);
        Assert.Equal(7, count);
    }

    [Fact]
    public void Values_echoed_elsewhere_are_redacted_by_value()
    {
        string echo = $$$"""
            {"mensagem":"sessão {{{SessionId}}} do login {{{Login}}} (app {{{AppId}}}), refresh {{{RefreshToken}}}, dono {{{UserId}}}",
             "dono":{{{UserId}}},"detalhe":{"sub":"{{{SubId}}}"}}
            """;
        TokenExchangeRedaction redaction = TokenExchangeRedaction.For(Response, [Secret]);

        (string text, _) = redaction.Redact(echo);

        Assert.All(Values, v => Assert.DoesNotContain(v, text));
        JsonNode root = JsonNode.Parse(text)!;                   // continua JSON válido
        Assert.Equal("[mascarado]", (string?)root["dono"]);       // o número ecoado vira o marcador, entre aspas
        Assert.Contains("[redigido]", (string?)root["mensagem"]);
        Assert.Contains("[mascarado]", (string?)root["mensagem"]);
    }

    [Fact]
    public void Plain_text_such_as_a_header_value_is_redacted_by_value()
    {
        TokenExchangeRedaction redaction = TokenExchangeRedaction.For(Response, [Secret]);

        (string text, int count) = redaction.Redact($"sess={SessionId}; user={UserId}; Bearer {AccessToken}");

        Assert.All(Values, v => Assert.DoesNotContain(v, text));
        Assert.Equal(3, count);
    }

    [Fact]
    public void A_short_identifier_is_masked_by_name_only()
    {
        // Um userId "4", por valor, apagaria o 4 do expires_in 86400. Curto, só pelo nome.
        const string response = """{"access_token":"tok-longo-o-bastante","expires_in":86400,"userId":4,"appId":"a1"}""";

        (string text, _) = TokenExchangeRedaction.For(response, []).Redact(response);

        JsonNode root = JsonNode.Parse(text)!;
        Assert.Equal(86400, (int?)root["expires_in"]);
        Assert.Equal("[mascarado]", (string?)root["userId"]);
        Assert.Equal("[mascarado]", (string?)root["appId"]);
    }

    [Fact]
    public void Response_without_anything_to_hide_stays_the_same()
    {
        const string response = """{"token_type":"bearer","expires_in":86400}""";

        Assert.Equal((response, 0), TokenExchangeRedaction.For(response, [null, ""]).Redact(response));
    }

    [Fact]
    public void The_envelope_redacts_the_header_values_with_the_same_rule()
    {
        TokenExchangeRedaction redaction = TokenExchangeRedaction.For(Response, [Secret]);
        (string body, int count) = redaction.Redact(Response);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://login.sandbox.exemplo/oauth/token?client_id=abc");
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        response.Headers.Add("Request-Id", SessionId);
        response.Headers.Add("Set-Cookie", $"sess={SessionId}");

        string envelope = PlatformResponseEnvelope.Build("token", request, response, body, count, redaction.Redact, DateTimeOffset.UnixEpoch);

        Assert.All(Values, v => Assert.DoesNotContain(v, envelope));
        JsonNode photo = JsonNode.Parse(envelope)!;
        Assert.Equal("[mascarado]", (string?)photo["response"]!["headers"]!["Request-Id"]);
        Assert.Null(photo["response"]!["headers"]!["Set-Cookie"]);                    // fora da lista, como sempre
        Assert.Equal("https://login.sandbox.exemplo/oauth/token", (string?)photo["request"]!["url"]);   // sem a query
        Assert.Equal(8, (int?)photo["redactions"]);                                    // os 7 do corpo e o do cabeçalho
    }
}
