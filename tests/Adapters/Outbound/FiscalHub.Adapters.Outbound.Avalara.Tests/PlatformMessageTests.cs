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

    [Fact]
    public void Problem_details_brings_the_title_and_each_field_error()
        => Assert.Equal(
            "One or more validation errors occurred.; $.itens[0].cfop: The value is invalid.",
            PlatformMessage.Extract(
                """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"$.itens[0].cfop":["The value is invalid."]},"traceId":"00-abc"}""",
                400));

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
