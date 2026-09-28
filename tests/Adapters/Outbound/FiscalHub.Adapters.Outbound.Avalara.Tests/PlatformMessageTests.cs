using FiscalHub.Adapters.Outbound.Avalara;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica a extração do motivo de uma recusa da plataforma (design D10). O formato real da resposta de erro da
/// Avalara ainda não foi gravado; a extração é tolerante e, sem formato reconhecido, devolve o próprio corpo.
/// </summary>
public class PlatformMessageTests
{
    [Fact]
    public void Empty_body_cites_the_http_status()
        => Assert.Equal("HTTP 422 sem corpo", PlatformMessage.Extract("", 422));

    [Fact]
    public void List_of_messages_is_joined_in_order()
        => Assert.Equal(
            "codigoEmpresa não cadastrado; CFOP 1556 incompatível com a operação",
            PlatformMessage.Extract("""{"mensagens":["codigoEmpresa não cadastrado","CFOP 1556 incompatível com a operação"]}""", 400));

    // ---------- o mapa de erros por campo vira resumo (establishment-and-readable-dashboard, D7) ----------

    [Fact]
    public void Problem_details_with_a_field_map_becomes_a_summary_without_the_title()
        => Assert.Equal(
            "1 campo com erro: $.itens[0].cfop",
            PlatformMessage.Extract(
                """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"$.itens[0].cfop":["The value is invalid."]},"traceId":"00-abc"}""",
                400));

    [Fact]
    public void Summary_names_three_fields_in_the_response_order_and_counts_the_rest()
    {
        string fields = string.Join(",", Enumerable.Range(0, 12).Select(i => $"\"itens[{i}].Item.TipoItem\":[\"'Tipo Item' não pode ser nulo.\"]"));

        string reason = PlatformMessage.Extract("""{"title":"One or more validation errors occurred.","errors":{""" + fields + "}}", 400);

        Assert.Equal("12 campos com erro: itens[0].Item.TipoItem, itens[1].Item.TipoItem, itens[2].Item.TipoItem e mais 9", reason);
        Assert.True(reason.Length < PlatformMessage.MaxLength);
    }

    [Theory]
    [InlineData("""{"errors":{"operacao":["x"],"tipoPagamento":["y"]}}""", "2 campos com erro: operacao, tipoPagamento")]
    [InlineData("""{"errors":{"operacao":["x"],"tipoPagamento":["y"],"parceiro.Codigo":["z","w"]}}""", "3 campos com erro: operacao, tipoPagamento, parceiro.Codigo")]
    public void Up_to_three_fields_have_no_remainder(string body, string expected)
        => Assert.Equal(expected, PlatformMessage.Extract(body, 400));

    [Fact]
    public void Problem_details_without_a_field_map_keeps_the_title_as_the_message()
        => Assert.Equal("Documento duplicado", PlatformMessage.Extract("""{"title":"Documento duplicado","status":400}""", 400));

    [Fact]
    public void Status_query_with_a_field_map_is_summarized_too()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"id":"3f2c","status":"erro","errors":{"cfop":["CFOP 1556 incompatível"]}}""");

        Assert.Equal("1 campo com erro: cfop", PlatformMessage.FindMessages(doc.RootElement));
    }

    [Fact]
    public void Message_objects_contribute_only_their_text()
        => Assert.Equal(
            "CFOP inválido",
            PlatformMessage.Extract("""{"erros":[{"codigo":"E101","descricao":"CFOP inválido"}]}""", 400));

    [Fact]
    public void Id_and_native_status_are_left_out()
        => Assert.Equal(
            "CFOP 1556 incompatível com a operação",
            PlatformMessage.Extract("""{"id":"3f2c","status":"erro","mensagem":"CFOP 1556 incompatível com a operação"}""", 200));

    [Fact]
    public void Json_without_recognized_message_is_returned_compacted()
        => Assert.Equal(
            """{"codigo":123,"campo":"cfop"}""",
            PlatformMessage.Extract("""{ "codigo": 123,  "campo": "cfop" }""", 400));

    [Fact]
    public void Plain_text_has_its_whitespace_collapsed()
        => Assert.Equal("Documento inválido", PlatformMessage.Extract("  Documento \r\n  inválido  ", 400));

    [Fact]
    public void Long_reason_is_cut_at_the_limit()
    {
        string reason = PlatformMessage.Extract(new string('x', 1500), 400);

        Assert.Equal(1000, reason.Length);
        Assert.EndsWith("…", reason);
    }
}
