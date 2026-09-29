using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Application.Inbound;
using static FiscalHub.Adapters.Ingress.D365Poll.Tests.D365Fixtures;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Impressão canônica (spec d365-document-assembly, design D11): mesma nota → mesma impressão; carimbo de
/// auditoria e anotações OData não contam; ordem da resposta não conta; valor fiscal conta.
/// </summary>
public class D365CanonicalizerTests
{
    [Fact]
    public void Same_responses_give_the_same_fingerprint()
    {
        Assert.Equal(Hash(Recorded()), Hash(Recorded()));
    }

    [Fact]
    public void Audit_stamp_and_etag_do_not_change_the_fingerprint()
    {
        D365DocumentRows original = Recorded();
        JsonObject header = Editable(original.Header);
        header["SysModifiedDateTime"] = "2026-09-26T10:00:00Z";
        header["@odata.etag"] = "W/\"outro\"";
        JsonObject tax = Editable(original.Taxes[0]);
        tax["@odata.etag"] = "W/\"outro\"";

        D365DocumentRows touched = original with
        {
            Header = ToElement(header),
            Taxes = [ToElement(tax), .. original.Taxes.Skip(1)],
        };

        Assert.Equal(Hash(original), Hash(touched));
    }

    [Fact]
    public void Changing_a_fiscal_value_changes_the_fingerprint()
    {
        D365DocumentRows original = Recorded();
        JsonObject tax = Editable(original.Taxes[0]);
        tax["TaxAmount"] = 999.99m;

        Assert.NotEqual(Hash(original), Hash(original with { Taxes = [ToElement(tax), .. original.Taxes.Skip(1)] }));
    }

    [Fact]
    public void Row_order_and_property_order_do_not_change_the_fingerprint()
    {
        D365DocumentRows original = Recorded();
        IReadOnlyList<JsonElement> shuffled = [.. original.Taxes.Reverse().Select(ReverseProperties)];

        Assert.Equal(Hash(original), Hash(original with { Taxes = shuffled, Header = ReverseProperties(original.Header) }));
    }

    [Fact]
    public void Numbers_are_written_exactly_as_they_came()
    {
        using JsonDocument doc = JsonDocument.Parse("""{"FiscalDocumentTaxTransRecId":1,"TaxAmount":100.50}""");
        D365DocumentRows rows = Recorded() with { Taxes = [doc.RootElement.Clone()] };

        Assert.Contains("\"TaxAmount\": 100.50", D365Canonicalizer.Canonicalize(rows));
    }

    [Fact]
    public void Accounting_rows_count_when_present()
    {
        D365DocumentRows withoutAccounting = Recorded() with { Accounting = [] };

        Assert.NotEqual(Hash(withoutAccounting), Hash(Recorded()));
    }

    [Fact]
    public void Canonical_carries_the_version_and_no_odata_annotation()
    {
        string canonical = D365Canonicalizer.Canonicalize(Recorded());

        using JsonDocument doc = JsonDocument.Parse(canonical);
        // v3: o cabeçalho ganhou o FiscalEstablishment (establishment-and-readable-dashboard, D3). A v2 tinha ampliado o
        // $select do cabeçalho e da linha (connector-not-validator, D12). Os campos novos entram na impressão.
        Assert.Equal(3, doc.RootElement.GetProperty("v").GetInt32());
        Assert.Equal("Matriz", doc.RootElement.GetProperty("header").GetProperty("FiscalEstablishment").GetString());
        Assert.True(doc.RootElement.GetProperty("header").TryGetProperty("AccountingDate", out _));
        Assert.True(doc.RootElement.GetProperty("header").TryGetProperty("TotalGoodsAmount", out _));
        Assert.All(doc.RootElement.GetProperty("lines").EnumerateArray(), line =>
        {
            Assert.True(line.TryGetProperty("Unit", out _));
            Assert.True(line.TryGetProperty("AccountingAmount", out _));
            Assert.True(line.TryGetProperty("Origin", out _));
        });
        Assert.Equal(["v", "header", "lines", "taxes", "charges", "accounting"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("@odata", canonical);
        Assert.DoesNotContain("SysModifiedDateTime", canonical);
    }

    private static D365DocumentRows Recorded() => new(
        Rows(Note(ImportNote, "header")).Single(),
        Rows(Note(ImportNote, "lines")),
        Rows(Note(ImportNote, "taxes")),
        Rows(Note(ImportNote, "charges")),
        Rows(Note(ImportNote, "taxtrans")));

    private static string Hash(D365DocumentRows rows) => ContentFingerprint.Of(D365Canonicalizer.Canonicalize(rows));

    private static JsonElement ReverseProperties(JsonElement row)
    {
        var reversed = new JsonObject();
        foreach (JsonProperty p in row.EnumerateObject().Reverse())
        {
            reversed[p.Name] = JsonNode.Parse(p.Value.GetRawText());
        }

        return ToElement(reversed);
    }
}
