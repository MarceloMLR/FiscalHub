using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Domain.Goods;
using static FiscalHub.Adapters.Ingress.D365Poll.Tests.D365Fixtures;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Montagem da NF-e a partir das respostas do D365 (spec d365-document-assembly, design D7–D10 e D14), sobre as
/// respostas GRAVADAS do fiscosysdev. Onde a base não tem o caso, o teste diz "derivada": parte de uma linha
/// gravada e faz a edição mínima à vista — ver "O que a base não exercita" no design.
/// </summary>
public class D365GoodsInvoiceAssemblerTests
{
    private static readonly D365PartyReferenceData SaoPauloAndRio = new(new D365PartyPlace("3550308", null), new D365PartyPlace("3304557", null));
    private static readonly D365PartyReferenceData NoReferenceData = new(D365PartyPlace.None, D365PartyPlace.None);

    // ---------- montagem completa (gravada) ----------

    [Fact]
    public void Recorded_outgoing_note_is_assembled_field_by_field()
    {
        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(Note(OutgoingNote), SaoPauloAndRio);

        Assert.Equal("35130344278225000180550020000000011000190380", invoice.AccessKey);
        Assert.Equal("55", invoice.Model);
        Assert.Equal("02", invoice.Series);
        Assert.Equal("000001", invoice.Number);
        Assert.Equal(new DateTimeOffset(2016, 3, 1, 12, 0, 0, TimeSpan.Zero), invoice.IssueDate);   // FiscalDocumentDate; o DateTime vem 1900
        Assert.Equal(12600m, invoice.TotalAmount);
        Assert.Equal(11250m, invoice.GoodsAmount);                                                    // TotalGoodsAmount
        Assert.Equal(new DateTimeOffset(2016, 3, 1, 12, 0, 0, TimeSpan.Zero), invoice.EntryExitDate); // AccountingDate
        Assert.Equal(Issuance.Own, invoice.Issuance);                                                 // FiscalDocumentIssuer = OwnEstablishment

        // Saída própria: o estabelecimento emite, o terceiro recebe. CNPJ sem a pontuação.
        Assert.Equal(new Party { TaxId = "44278225000180", Name = "Contoso Entertainment System Brazil", StateRegistration = "652128379113", MunicipalityCode = "3550308" }, invoice.Issuer);
        Assert.Equal(new Party { TaxId = "72458488000106", Name = "Southridge Video Brasil Ltda", StateRegistration = "55166625", MunicipalityCode = "3304557" }, invoice.Recipient);
        Assert.Null(invoice.IbsCbsTaxableMunicipality);

        Assert.Equal([1, 2, 3], invoice.Items.Select(i => i.Number));
        GoodsInvoiceItem first = invoice.Items[0];
        Assert.Equal("BRMF010", first.ProductCode);
        Assert.Equal("Caixa acústica padrão 100W", first.Description);
        Assert.Equal("85182200", first.Ncm);   // 8518.22.00
        Assert.Equal("6101", first.Cfop);      // 6.101
        Assert.Equal(10m, first.Quantity);
        Assert.Equal(350m, first.UnitAmount);
        Assert.Equal(3500m, first.TotalAmount);
        Assert.Equal("pcs", first.Unit);
        Assert.Equal(4025m, first.AccountingAmount);
        Assert.Equal("0", first.Origin);                  // National → 0 (tabela de origem do leiaute)
        Assert.Equal("1", invoice.Items[1].Origin);       // DirectImport → 1: a caixa importada
        Assert.Null(first.ReformTaxes);        // nota de 2016: grupo ausente, não zerado
        Assert.Empty(first.Withholdings);
        Assert.Empty(first.Charges);

        Assert.Equal(
            [
                new TaxLine { Kind = TaxKind.Cofins, Cst = "01", TaxBase = 3500m, Rate = 7.6m, Amount = 266m },
                new TaxLine { Kind = TaxKind.Icms, Cst = "00", TaxBase = 3500m, Rate = 12m, Amount = 420m },
                new TaxLine { Kind = TaxKind.Ipi, Cst = "51", TaxBase = 3500m, Rate = 15m, Amount = 525m },
                new TaxLine { Kind = TaxKind.Pis, Cst = "01", TaxBase = 3500m, Rate = 1.65m, Amount = 57.75m },
            ],
            first.Taxes);
        Assert.Equal(3, invoice.Items[1].Taxes.Count);   // a linha 2 não tem IPI
        Assert.Equal(4, invoice.Items[2].Taxes.Count);
    }

    [Theory]
    [InlineData(SameCustomerNoteA)]
    [InlineData(SameCustomerNoteB)]
    [InlineData(ImportNote)]
    [InlineData(ThirdPartyIncomingNote)]
    public void Other_recorded_notes_assemble_with_every_tax_in_one_place(long recId)
    {
        D365DocumentRows rows = Note(recId);

        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(rows, NoReferenceData);

        Assert.Equal(rows.Lines.Count, invoice.Items.Count);
        int placed = invoice.Items.Sum(i => i.Taxes.Count + i.Withholdings.Count + i.Charges.Sum(c => c.Taxes.Count + c.Withholdings.Count));
        Assert.Equal(rows.Taxes.Count, placed);
    }

    // ---------- mapeamento ----------

    [Fact]
    public void Third_party_issued_incoming_note_has_the_third_party_as_issuer()
    {
        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(Note(ThirdPartyIncomingNote), SaoPauloAndRio);

        Assert.Equal("11613525000119", invoice.Issuer.TaxId);
        Assert.Equal("3304557", invoice.Issuer.MunicipalityCode);   // o município do terceiro vai com o terceiro
        Assert.Equal("44278225000180", invoice.Recipient.TaxId);
        Assert.Equal("3550308", invoice.Recipient.MunicipalityCode);
        Assert.Equal(Issuance.ThirdParty, invoice.Issuance);
    }

    [Fact]
    public void Recorded_third_party_note_carries_the_own_establishment_as_recipient()
    {
        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(Note(ThirdPartyIncomingNote), NoReferenceData);

        // FiscalEstablishmentCNPJCPF "442782250001-80" e FiscalEstablishment "Matriz", como gravados.
        Assert.Equal(new Establishment { TaxId = "44278225000180", Code = "Matriz" }, invoice.Establishment);
        Assert.Equal(invoice.Recipient.TaxId, invoice.Establishment!.TaxId);   // na nota de terceiro, o próprio é o destinatário
        Assert.NotEqual(invoice.Issuer.TaxId, invoice.Establishment.TaxId);
    }

    [Fact]
    public void Alphanumeric_cnpj_keeps_its_letters_in_the_establishment_and_the_parties()
    {
        // Derivada: a nota de saída gravada, com o CNPJ do estabelecimento e o do terceiro trocados por alfanuméricos.
        D365DocumentRows rows = Note(OutgoingNote);
        JsonObject header = Editable(rows.Header);
        header["FiscalEstablishmentCNPJCPF"] = "12.ABC.345/01DE-35";
        header["ThirdPartyCNPJCPF"] = "98.XYZ.765/0001-32";

        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(rows with { Header = ToElement(header) }, NoReferenceData);

        Assert.Equal(new Establishment { TaxId = "12ABC34501DE35", Code = "Matriz" }, invoice.Establishment);
        Assert.Equal("12ABC34501DE35", invoice.Issuer.TaxId);      // saída própria: o estabelecimento emite
        Assert.Equal("98XYZ765000132", invoice.Recipient.TaxId);
        Assert.Equal("85182200", invoice.Items[0].Ncm);            // o NCM e o CFOP continuam só com dígitos
        Assert.Equal("6101", invoice.Items[0].Cfop);
    }

    [Fact]
    public void Recorded_note_with_empty_date_time_has_the_fiscal_date_of_the_fiscal_document_date()
    {
        // O FiscalDocumentDateTime vem 1900; a data fiscal é o FiscalDocumentDate (2016-09-02T12:00:00Z).
        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(Note(ThirdPartyIncomingNote), NoReferenceData);

        Assert.Equal(new DateOnly(2016, 9, 2), invoice.FiscalDate);
    }

    [Fact]
    public void Fiscal_date_is_the_fiscal_document_date_and_not_the_utc_day_of_the_issue_instant()
    {
        // Derivada: a nota emitida às 22:30 de 2026-08-07 em Brasília, que o F&O guarda em UTC.
        D365DocumentRows rows = Note(OutgoingNote);
        JsonObject header = Editable(rows.Header);
        header["FiscalDocumentDateTime"] = "2026-08-08T01:30:00Z";
        header["FiscalDocumentDate"] = "2026-08-07T12:00:00Z";

        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(rows with { Header = ToElement(header) }, NoReferenceData);

        Assert.Equal(new DateOnly(2026, 8, 7), invoice.FiscalDate);
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 1, 30, 0, TimeSpan.Zero), invoice.IssueDate);   // o instante segue como veio
    }

    [Fact]
    public void Party_addresses_come_with_their_reference_data()
    {
        var establishment = new Address { Street = "Av. das Nações Unidas", Number = "12901", District = "Brooklin", PostalCode = "04795100" };
        var thirdParty = new Address { Street = "Estrada do Galeão", Number = "135", District = "Ilha do Governador", PostalCode = "21931385" };

        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(
            Note(OutgoingNote), new D365PartyReferenceData(new D365PartyPlace("3550308", establishment), new D365PartyPlace("3304557", thirdParty)));

        Assert.Equal(establishment, invoice.Issuer.Address);     // saída própria: o estabelecimento emite
        Assert.Equal(thirdParty, invoice.Recipient.Address);
    }

    [Fact]
    public void Empty_accounting_date_leaves_the_entry_exit_date_absent()
    {
        // Derivada: AccountingDate trocado pelo valor vazio do F&O.
        D365DocumentRows rows = Note(OutgoingNote);
        JsonObject header = Editable(rows.Header);
        header["AccountingDate"] = "1900-01-01T12:00:00Z";

        Assert.Null(D365GoodsInvoiceAssembler.Assemble(rows with { Header = ToElement(header) }, NoReferenceData).EntryExitDate);
    }

    [Fact]
    public void Empty_unit_and_origin_without_translation_stay_absent()
    {
        // Derivada: Unit esvaziada e Origin com um nome que a base não mostrou (sem tradução com evidência → ausente,
        // nunca 0).
        D365DocumentRows rows = Note(OutgoingNote);
        JsonObject line = Editable(rows.Lines[0]);
        line["Unit"] = "";
        line["Origin"] = "ForeignWithoutNationalSimilar";

        GoodsInvoiceItem item = D365GoodsInvoiceAssembler.Assemble(rows with { Lines = [ToElement(line), .. rows.Lines.Skip(1)] }, NoReferenceData).Items[0];

        Assert.Null(item.Unit);
        Assert.Null(item.Origin);
    }

    [Fact]
    public void Fractional_line_number_fails()
    {
        // Derivada: LineNum 1 → 1.5 na linha gravada.
        D365DocumentRows rows = Note(OutgoingNote);
        JsonObject line = Editable(rows.Lines[0]);
        line["LineNum"] = 1.5m;

        var ex = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(rows with { Lines = [ToElement(line), .. rows.Lines.Skip(1)] }, NoReferenceData));

        Assert.Contains("LineNum", ex.Message);
    }

    [Fact]
    public void Header_without_lines_assembles_without_items()
    {
        D365DocumentRows rows = Note(OutgoingNote) with { Lines = [], Taxes = [], Accounting = [] };

        Assert.Empty(D365GoodsInvoiceAssembler.Assemble(rows, NoReferenceData).Items);   // a validação rejeita depois
    }

    // ---------- distribuição dos impostos ----------

    [Fact]
    public void Charge_tax_with_empty_line_key_goes_to_the_charge()
    {
        // Derivada (nenhum dos 547 impostos aponta para encargo): o ICMS gravado da nota de importação passa a
        // apontar para o encargo gravado 35637149828, com a FK de linha nula.
        D365DocumentRows rows = Note(ImportNote);
        JsonObject tax = Editable(rows.Taxes.Single(t => t.GetProperty("FiscalTaxType").GetString() == "ICMS"));
        tax["FiscalDocumentLineRecId"] = null;
        tax["FiscalDocumentMiscChargeRecId"] = 35637149828L;
        D365DocumentRows derived = rows with { Taxes = [.. rows.Taxes.Where(t => t.GetProperty("FiscalTaxType").GetString() != "ICMS"), ToElement(tax)] };

        GoodsInvoiceItem item = D365GoodsInvoiceAssembler.Assemble(derived, NoReferenceData).Items.Single();

        ItemCharge charge = item.Charges.Single();
        Assert.Equal(1, charge.Number);
        Assert.Equal(416.25m, charge.Amount);
        Assert.Equal(TaxKind.Icms, charge.Taxes.Single().Kind);
        Assert.Equal(1568.14m, charge.Taxes.Single().Amount);
        Assert.DoesNotContain(item.Taxes, t => t.Kind == TaxKind.Icms);
    }

    [Fact]
    public void Retained_tax_becomes_a_withholding_and_never_a_tax()
    {
        // Gravada: nota 01 5637146826 do snapshot, com um IRRF retido (as retenções da base estão em notas 01/SE;
        // a distribuição não depende do modelo).
        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(FromSnapshot(5637146826), NoReferenceData);

        TaxLine irrf = invoice.Items.SelectMany(i => i.Withholdings).Single();
        Assert.Equal(TaxKind.Irrf, irrf.Kind);
        Assert.Equal(22.5m, irrf.Amount);
        Assert.True(irrf.TaxBase > 0);
        Assert.DoesNotContain(invoice.Items.SelectMany(i => i.Taxes), t => t.Kind == TaxKind.Irrf);
    }

    [Fact]
    public void Recorded_base_sweep_places_all_547_taxes_with_the_12_withholdings_apart()
    {
        // Reproduz offline a validação feita no ambiente: todos os impostos reais sobre as linhas e os encargos
        // reais, nas 83 notas (a montagem não olha o modelo; quem filtra é o source).
        IReadOnlyList<JsonElement> headers = Rows("snapshot/headers.json");
        int taxes = 0, withholdings = 0, retainedAsTax = 0;

        foreach (JsonElement header in headers)
        {
            GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(FromSnapshot(header.GetProperty("FiscalDocumentRecId").GetInt64()), NoReferenceData);
            IEnumerable<TaxLine> itemAndChargeTaxes = invoice.Items.SelectMany(i => i.Taxes.Concat(i.Charges.SelectMany(c => c.Taxes)));
            taxes += itemAndChargeTaxes.Count();
            withholdings += invoice.Items.Sum(i => i.Withholdings.Count + i.Charges.Sum(c => c.Withholdings.Count));
            retainedAsTax += itemAndChargeTaxes.Count(t => t.Kind == TaxKind.Irrf);
        }

        Assert.Equal(83, headers.Count);
        Assert.Equal(547, taxes + withholdings);
        Assert.Equal(12, withholdings);
        Assert.Equal(0, retainedAsTax);
    }

    [Theory]
    [InlineData(0L, 0L, "sem linha nem encargo")]
    [InlineData(35637156586L, 35637149828L, "linha e encargo")]
    [InlineData(99L, 0L, "fora do documento")]
    public void Tax_without_a_single_place_fails_citing_its_rec_id(long lineKey, long chargeKey, string why)
    {
        // Derivada: as chaves do COFINS gravado 35637156610 trocadas.
        D365DocumentRows rows = Note(OutgoingNote);
        JsonObject tax = Editable(rows.Taxes[0]);
        tax["FiscalDocumentLineRecId"] = lineKey;
        tax["FiscalDocumentMiscChargeRecId"] = chargeKey;

        var ex = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(rows with { Taxes = [ToElement(tax), .. rows.Taxes.Skip(1)] }, NoReferenceData));

        Assert.Contains("35637156610", ex.Message);
        Assert.Contains(why, ex.Message);
    }

    [Fact]
    public void Blank_tax_type_fails()
    {
        // Derivada: FiscalTaxType do COFINS gravado → Blank.
        D365DocumentRows rows = Note(OutgoingNote);
        JsonObject tax = Editable(rows.Taxes[0]);
        tax["FiscalTaxType"] = "Blank";

        var ex = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(rows with { Taxes = [ToElement(tax), .. rows.Taxes.Skip(1)] }, NoReferenceData));

        Assert.Contains("Blank", ex.Message);
    }

    [Fact]
    public void Sign_and_cst_come_from_the_fiscal_entity()
    {
        // Gravada: IPI da linha 1 da nota de saída — fiscal CST 51 e +525; a contábil do mesmo TaxTransRecId, −525.
        D365DocumentRows rows = Note(OutgoingNote);
        JsonElement accounting = rows.Accounting.Single(a => a.GetProperty("TaxTransRecId").GetInt64() == 35637488241);
        Assert.Equal(-525m, accounting.GetProperty("TaxAmount").GetDecimal());

        TaxLine ipi = D365GoodsInvoiceAssembler.Assemble(rows, NoReferenceData).Items[0].Taxes.Single(t => t.Kind == TaxKind.Ipi);

        Assert.Equal("51", ipi.Cst);
        Assert.Equal(525m, ipi.Amount);
    }

    // ---------- encargos ----------

    [Fact]
    public void Recorded_charge_lands_on_its_line()
    {
        GoodsInvoiceItem item = D365GoodsInvoiceAssembler.Assemble(Note(ImportNote), NoReferenceData).Items.Single();

        ItemCharge charge = item.Charges.Single();
        Assert.Equal(1, charge.Number);
        Assert.Equal(ChargeKind.Other, charge.Kind);
        Assert.Equal(416.25m, charge.Amount);
        Assert.Null(charge.Description);   // Txt vazio
    }

    [Fact]
    public void Charge_outside_the_document_or_of_unknown_type_fails()
    {
        // Derivadas: linha do encargo gravado trocada; tipo trocado para um valor sem evidência na base.
        D365DocumentRows rows = Note(ImportNote);
        JsonObject orphan = Editable(rows.Charges[0]);
        orphan["FiscalDocumentLineRecId"] = 99L;
        JsonObject unknown = Editable(rows.Charges[0]);
        unknown["MiscChargeType"] = "Freight";

        var orphanEx = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(rows with { Charges = [ToElement(orphan)] }, NoReferenceData));
        var unknownEx = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(rows with { Charges = [ToElement(unknown)] }, NoReferenceData));

        Assert.Contains("35637149828", orphanEx.Message);
        Assert.Contains("Freight", unknownEx.Message);
    }

    // ---------- grupo IBS/CBS (todas derivadas: a base é de 2016) ----------

    [Fact]
    public void Complete_reform_group_is_assembled_without_class_trib()
    {
        GoodsInvoiceItem item = D365GoodsInvoiceAssembler.Assemble(WithReform(Reform("CBS", 0.9m, 31.5m), Reform("IBSState", 0.1m, 3.5m), Reform("IBSCity", 0.05m, 1.75m)), NoReferenceData).Items[0];

        Assert.NotNull(item.ReformTaxes);
        Assert.Equal("000", item.ReformTaxes.Cst);
        Assert.Equal(string.Empty, item.ReformTaxes.ClassTrib);   // a entidade fiscal não traz o cClassTrib
        Assert.Equal(3500m, item.ReformTaxes.TaxBase);
        Assert.Equal(3.5m, item.ReformTaxes.IbsCbs.IbsState.Amount);
        Assert.Equal(1.75m, item.ReformTaxes.IbsCbs.IbsMunicipality.Amount);
        Assert.Equal(5.25m, item.ReformTaxes.IbsCbs.IbsTotalAmount);   // vIBS = UF + Mun
        Assert.Equal(0.9m, item.ReformTaxes.IbsCbs.Cbs.Rate);
        Assert.Equal(31.5m, item.ReformTaxes.IbsCbs.Cbs.Amount);
        Assert.DoesNotContain(item.Taxes, t => t.Amount is 31.5m or 3.5m or 1.75m);   // não duplica nos Taxes
    }

    [Fact]
    public void Partial_reform_group_fails()
    {
        var ex = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(WithReform(Reform("CBS", 0.9m, 31.5m)), NoReferenceData));

        Assert.Contains("CBS", ex.Message);
    }

    [Fact]
    public void Reform_group_with_divergent_cst_or_base_fails()
    {
        Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(
            WithReform(Reform("CBS", 0.9m, 31.5m), Reform("IBSState", 0.1m, 3.5m, cst: "200"), Reform("IBSCity", 0.05m, 1.75m)), NoReferenceData));
        Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(
            WithReform(Reform("CBS", 0.9m, 31.5m), Reform("IBSState", 0.1m, 3.5m, taxBase: 3000m), Reform("IBSCity", 0.05m, 1.75m)), NoReferenceData));
    }

    [Fact]
    public void Retained_reform_tax_fails()
    {
        Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(
            WithReform(Reform("CBS", 0.9m, 31.5m), Reform("IBSState", 0.1m, 3.5m, retained: true), Reform("IBSCity", 0.05m, 1.75m)), NoReferenceData));
    }

    // ---------- complemento contábil ----------

    [Fact]
    public void Recorded_import_tax_zeroed_in_fiscal_is_completed_from_accounting_field_by_field()
    {
        // Gravada: nota de importação. Fiscal 0 / 30 / 0 (base em TaxBaseAmountOther 4.500); contábil 4.500 / 30 / 1.350.
        D365DocumentRows rows = Note(ImportNote);
        Assert.True(D365GoodsInvoiceAssembler.NeedsAccounting(rows.Taxes));

        TaxLine importTax = D365GoodsInvoiceAssembler.Assemble(rows, NoReferenceData).Items.Single().Taxes.Single(t => t.Kind == TaxKind.ImportTax);

        Assert.Equal(new TaxLine { Kind = TaxKind.ImportTax, Cst = null, TaxBase = 4500m, Rate = 30m, Amount = 1350m, OtherBase = 4500m }, importTax);
    }

    [Fact]
    public void Zeros_that_are_not_import_tax_stay_zero_and_need_no_accounting()
    {
        // Gravada: entrada de terceiro com COFINS/PIS 98, ICMS/ICMSDiff 90 zerados na fiscal e com valor na contábil.
        D365DocumentRows rows = Note(ThirdPartyIncomingNote);
        Assert.False(D365GoodsInvoiceAssembler.NeedsAccounting(rows.Taxes));

        GoodsInvoiceItem item = D365GoodsInvoiceAssembler.Assemble(rows with { Accounting = [] }, NoReferenceData).Items.Single();

        Assert.All(item.Taxes, t => Assert.Equal(0m, t.Amount));
        Assert.Equal(4, item.Taxes.Count);
    }

    [Fact]
    public void Recorded_ipi_cst_05_zeroed_stays_zero()
    {
        // Gravada: uma nota do snapshot com IPI CST 05 zerado na fiscal e valor na contábil.
        JsonElement zeroedIpi = Rows("snapshot/taxes.json").First(t =>
            t.GetProperty("FiscalTaxType").GetString() == "IPI" && t.GetProperty("TaxationCode").GetString() == "05" && t.GetProperty("TaxAmount").GetDecimal() == 0);
        long doc = zeroedIpi.GetProperty("FiscalDocumentRecId").GetInt64();

        GoodsInvoice invoice = D365GoodsInvoiceAssembler.Assemble(FromSnapshot(doc), NoReferenceData);

        TaxLine ipi = invoice.Items.SelectMany(i => i.Taxes).First(t => t.Kind == TaxKind.Ipi && t.Cst == "05");
        Assert.Equal(0m, ipi.Amount);
    }

    [Fact]
    public void Notes_without_zeroed_import_tax_need_no_accounting()
    {
        Assert.False(D365GoodsInvoiceAssembler.NeedsAccounting(Note(OutgoingNote).Taxes));
    }

    // ---------- divergência não prevista (todas derivadas da nota de importação) ----------

    [Fact]
    public void Bridge_without_accounting_pair_fails()
    {
        D365DocumentRows rows = Note(ImportNote);

        var ex = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(
            rows with { Accounting = [.. rows.Accounting.Where(a => a.GetProperty("TaxTransRecId").GetInt64() != 35637488268)] }, NoReferenceData));

        Assert.Contains("BRMF06-110000031", ex.Message);
        Assert.Contains("35637488268", ex.Message);
    }

    [Theory]
    [InlineData("TaxType", "IPI", "tipo")]
    [InlineData("TaxAmount", "-1350", "negativ")]
    [InlineData("TaxValue", "25", "TaxValue")]
    public void Unexpected_divergence_fails_citing_voucher_and_rec_ids(string field, string value, string cited)
    {
        D365DocumentRows rows = Note(ImportNote);
        JsonObject accounting = Editable(rows.Accounting.Single(a => a.GetProperty("TaxTransRecId").GetInt64() == 35637488268));
        accounting[field] = field == "TaxType" ? value : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        var ex = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(
            rows with { Accounting = [.. rows.Accounting.Where(a => a.GetProperty("TaxTransRecId").GetInt64() != 35637488268), ToElement(accounting)] }, NoReferenceData));

        Assert.Contains("BRMF06-110000031", ex.Message);
        Assert.Contains("35637156639", ex.Message);   // o imposto fiscal
        Assert.Contains("35637488268", ex.Message);   // a linha contábil
        Assert.Contains(cited, ex.Message);
    }

    [Fact]
    public void Two_different_non_zero_bases_fail()
    {
        // Spec "Dois valores diferentes": ImportTax elegível (valor zero) com base 4.000 na fiscal e 4.500 na contábil.
        D365DocumentRows rows = Note(ImportNote);
        JsonObject tax = Editable(rows.Taxes.Single(t => t.GetProperty("FiscalTaxType").GetString() == "ImportTax"));
        tax["TaxBaseAmount"] = 4000m;

        var ex = Assert.Throws<D365AssemblyException>(() => D365GoodsInvoiceAssembler.Assemble(
            rows with { Taxes = [.. rows.Taxes.Where(t => t.GetProperty("FiscalTaxType").GetString() != "ImportTax"), ToElement(tax)] }, NoReferenceData));

        Assert.Contains("TaxBaseAmount", ex.Message);
        Assert.Contains("4000", ex.Message);
        Assert.Contains("4500", ex.Message);
    }

    // ---------- apoio ----------

    private static D365DocumentRows Note(long recId) => new(
        Rows(D365Fixtures.Note(recId, "header")).Single(),
        Rows(D365Fixtures.Note(recId, "lines")),
        Rows(D365Fixtures.Note(recId, "taxes")),
        Rows(D365Fixtures.Note(recId, "charges")),
        Rows(D365Fixtures.Note(recId, "taxtrans")));

    /// <summary>Documento do snapshot da base (qualquer modelo), com a contábil de todos os vouchers fiscais.</summary>
    private static D365DocumentRows FromSnapshot(long recId)
    {
        static long Long(JsonElement row, string name) => row.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

        return new D365DocumentRows(
            Rows("snapshot/headers.json").Single(h => Long(h, "FiscalDocumentRecId") == recId),
            [.. Rows("snapshot/lines.json").Where(l => Long(l, "FiscalDocumentRecId") == recId)],
            [.. Rows("snapshot/taxes.json").Where(t => Long(t, "FiscalDocumentRecId") == recId || Long(t, "MiscChargeFiscalDocumentRecId") == recId)],
            [.. Rows("snapshot/charges.json").Where(c => Long(c, "FiscalDocumentRecId") == recId)],
            SnapshotAccounting());
    }

    /// <summary>Derivada: a nota de saída gravada com linhas de IBS/CBS acrescentadas à linha 1, a partir do COFINS gravado dela.</summary>
    private static D365DocumentRows WithReform(params JsonObject[] reform)
    {
        D365DocumentRows rows = Note(OutgoingNote);
        return rows with { Taxes = [.. rows.Taxes, .. reform.Select(ToElement)] };
    }

    private static int _nextRecId = 90_000_000;

    private static JsonObject Reform(string type, decimal rate, decimal amount, string cst = "000", decimal taxBase = 3500m, bool retained = false)
    {
        JsonObject tax = Editable(Note(OutgoingNote).Taxes[0]);   // COFINS gravado da linha 1
        tax["FiscalDocumentTaxTransRecId"] = Interlocked.Increment(ref _nextRecId);
        tax["TaxTransRecId"] = 0L;
        tax["FiscalTaxType"] = type;
        tax["TaxationCode"] = cst;
        tax["TaxBaseAmount"] = taxBase;
        tax["TaxValue"] = rate;
        tax["TaxAmount"] = amount;
        tax["RetainedTax"] = retained ? "Yes" : "No";
        return tax;
    }
}
