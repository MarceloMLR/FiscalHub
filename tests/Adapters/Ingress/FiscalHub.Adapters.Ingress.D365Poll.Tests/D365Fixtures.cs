using System.Text.Json;
using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Acesso às respostas gravadas do fiscosysdev (<c>tools/d365-fixtures</c>, design D15). Caminhos relativos a
/// <c>Fixtures/d365/</c>. As derivadas (<c>*.derived.json</c>) são montadas em memória pelos testes a partir de
/// uma gravada, com a edição à vista no próprio teste — nada é inventado do zero.
/// </summary>
internal static class D365Fixtures
{
    /// <summary>Nota 55 de saída própria, 3 linhas, 11 impostos (IPI 51 × contábil 01 com sinal invertido).</summary>
    public const long OutgoingNote = 35637156582;

    /// <summary>Nota 55 de importação (entrada própria): ImportTax zerado na fiscal, 1 encargo, terceiro sem CNPJ e sem cidade.</summary>
    public const long ImportNote = 35637156586;

    /// <summary>Nota 55 de entrada de terceiro: impostos zerados na fiscal com valor na contábil (não ImportTax).</summary>
    public const long ThirdPartyIncomingNote = 5637156579;

    /// <summary>Duas notas 55 de saída para o mesmo terceiro (endereço 22565429296) — prova do cache.</summary>
    public const long SameCustomerNoteA = 35637156583;
    public const long SameCustomerNoteB = 35637156584;

    /// <summary>Nota 01 cancelada (fora do escopo pelo modelo).</summary>
    public const long CancelledModel01 = 5637155831;

    /// <summary>Nota SE autorizada (fora do escopo pelo modelo).</summary>
    public const long ServiceNote = 5637149826;

    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "d365");

    /// <summary>Texto cru da resposta gravada.</summary>
    public static string Text(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    /// <summary>Linhas (<c>value</c>) da resposta gravada.</summary>
    public static IReadOnlyList<JsonElement> Rows(string relative)
    {
        using JsonDocument doc = JsonDocument.Parse(Text(relative));
        return [.. doc.RootElement.GetProperty("value").EnumerateArray().Select(r => r.Clone())];
    }

    /// <summary>Linhas de todos os blocos da contábil dos vouchers fiscais.</summary>
    public static IReadOnlyList<JsonElement> SnapshotAccounting()
        => [.. Directory.GetFiles(Path.Combine(Root, "snapshot"), "taxtrans-*.json").Order().SelectMany(f => Rows(Path.GetRelativePath(Root, f)))];

    public static string Note(long recId, string file) => $"notes/{recId}/{file}.json";

    /// <summary>Resposta de uma única linha, montada a partir de um nó (para fixtures derivadas).</summary>
    public static string Response(params JsonNode[] rows)
        => new JsonObject { ["value"] = new JsonArray([.. rows.Select(r => r.DeepClone())]) }.ToJsonString();

    /// <summary>Cópia editável de uma linha gravada.</summary>
    public static JsonObject Editable(JsonElement row) => JsonNode.Parse(row.GetRawText())!.AsObject();

    public static JsonElement ToElement(JsonNode node)
    {
        using JsonDocument doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }
}
