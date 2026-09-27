using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// JSON canônico de um documento do D365 (ADR-0025 §7, design D11): o "cru" sobre o qual a impressão de conteúdo
/// (ADR-0016) é calculada, e que vira a foto da fonte (ADR-0006). Estável enquanto nada fiscal mudar:
/// entidades em ordem fixa, coleções pelo próprio RecId, propriedades em ordem ordinal, números como vieram, e
/// sem <c>@odata.*</c> nem <c>SysModifiedDateTime</c> — senão um toque de auditoria reenviaria a nota.
/// Mudar este formato (ou o <c>$select</c>) muda a impressão de toda nota redescoberta: suba <see cref="Version"/>.
/// </summary>
internal static class D365Canonicalizer
{
    public const int Version = 1;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // foto legível ("acústica"); determinístico igual
    };

    public static string Canonicalize(D365DocumentRows rows)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WritePropertyName("header");
            WriteRow(writer, rows.Header);
            WriteCollection(writer, "lines", rows.Lines, "FiscalDocumentLineRecId");
            WriteCollection(writer, "taxes", rows.Taxes, "FiscalDocumentTaxTransRecId");
            WriteCollection(writer, "charges", rows.Charges, "FiscalDocumentMiscChargeRecId");
            WriteCollection(writer, "accounting", rows.Accounting, "TaxTransRecId");
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCollection(Utf8JsonWriter writer, string name, IReadOnlyList<JsonElement> rows, string recIdProperty)
    {
        writer.WriteStartArray(name);
        foreach (JsonElement row in rows.OrderBy(r => RecIdOf(r, recIdProperty)))
        {
            WriteRow(writer, row);
        }

        writer.WriteEndArray();
    }

    private static void WriteRow(Utf8JsonWriter writer, JsonElement row)
    {
        writer.WriteStartObject();
        foreach (JsonProperty property in row.EnumerateObject()
                     .Where(p => !IsVolatile(p.Name))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            writer.WritePropertyName(property.Name);
            property.Value.WriteTo(writer);   // número com o texto cru da resposta
        }

        writer.WriteEndObject();
    }

    private static bool IsVolatile(string name)
        => name.StartsWith("@odata.", StringComparison.Ordinal) || name == "SysModifiedDateTime";

    private static long RecIdOf(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long recId) ? recId : 0;
}
