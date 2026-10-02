using System.Text.Json;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Mapeamento domínio → contrato da Avalara (spec avalara-document-contract, design D3 e D6 a D9). O contrato vem dos
/// JSONs reais: clássicos no bloco <c>imposto</c>, o array <c>impostos</c> só para o grupo da Reforma. O que o documento
/// não tem fica fora; o que ele tem e o contrato não leva é declarado como omissão. A nota de exemplo é a
/// <c>brmf|BRMF21-10000026</c> gravada do fiscosysdev.
/// </summary>
public class GoodsInvoiceToAvalaraTests
{
    private const string Key = "brmf|BRMF21-10000026";
    private static readonly AvalaraCompanyCodes Codes = new("20247332000182", "20247332000182");

    // ---------- cabeçalho e parceiro ----------

    [Fact]
    public void Maps_the_header_the_codes_and_the_partner()
    {
        AvalaraDocument doc = MapOk(OutgoingNote());

        Assert.Equal("20247332000182", doc.CodigoEmpresa);          // da configuração, não do ERP
        Assert.Equal("20247332000182", doc.CodigoContribuinte);
        Assert.Equal("02", doc.Serie);
        Assert.Equal(55, doc.Modelo);
        Assert.Equal("000001", doc.NumeroDocumento);                // como veio, com os zeros
        Assert.Equal("35130344278225000180550020000000011000190380", doc.ChaveNFe);
        Assert.Equal(new DateTime(2016, 3, 1, 12, 0, 0, DateTimeKind.Utc), doc.DataEmissao);
        Assert.Equal(new DateTime(2016, 3, 1, 12, 0, 0, DateTimeKind.Utc), doc.DataEntradaSaida);
        Assert.Equal(new DateTime(2016, 3, 1, 0, 0, 0, DateTimeKind.Utc), doc.PeriodoEscrituracao);
        Assert.Equal(0, doc.Situacao);
        Assert.Equal(Key, doc.CodigoReferenciaIntegracao);
        Assert.Equal(12600m, doc.Totais.ValorDocumento);
        Assert.Equal(11250m, doc.Totais.ValorMercadorias);

        Assert.Equal(
            new AvalaraParceiro { Nome = "Southridge Video Brasil Ltda", Cnpj = "72458488000106", Endereco = "Estrada do Galeão", Numero = "135", Bairro = "Ilha do Governador", Cep = "21931385" },
            doc.Parceiro);
    }

    [Fact]
    public void Empty_access_key_is_left_out()
        => Assert.Null(MapOk(OutgoingNote() with { AccessKey = "" }).ChaveNFe);

    [Fact]
    public void Bookkeeping_period_is_the_first_day_of_the_entry_exit_month()
        => Assert.Equal(
            new DateTime(2016, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            MapOk(OutgoingNote() with { EntryExitDate = new DateTimeOffset(2016, 9, 2, 12, 0, 0, TimeSpan.Zero) }).PeriodoEscrituracao);

    [Fact]
    public void Without_entry_exit_date_there_is_no_entry_exit_date_nor_period()
    {
        AvalaraDocument doc = MapOk(OutgoingNote() with { EntryExitDate = null });

        Assert.Null(doc.DataEntradaSaida);
        Assert.Null(doc.PeriodoEscrituracao);
    }

    [Theory]
    [InlineData("72458488000106", "72458488000106", null)]
    [InlineData("72.458.488/0001-06", "72458488000106", null)]
    [InlineData("12ABC34501DE35", "12ABC34501DE35", null)]       // CNPJ alfanumérico: 14 caracteres, não vai sem documento
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35", null)]
    [InlineData("12345678901", null, "12345678901")]
    [InlineData("", null, null)]   // fornecedor estrangeiro: sem CNPJ nem CPF
    public void Partner_document_goes_by_its_length(string taxId, string? cnpj, string? cpf)
    {
        AvalaraParceiro parceiro = GoodsInvoiceToAvalara.Map(OutgoingNote(), new AvalaraHeaderData(Codes, Southridge() with { TaxId = taxId }, Key)).Document!.Parceiro;

        Assert.Equal(cnpj, parceiro.Cnpj);
        Assert.Equal(cpf, parceiro.Cpf);
    }

    // ---------- itens ----------

    [Fact]
    public void Maps_the_item_with_its_unit_cfop_and_accounting_amount()
    {
        AvalaraItem item = MapOk(OutgoingNote()).Itens[0];

        Assert.Equal(1, item.NumeroSequencia);
        Assert.Equal(new AvalaraItemCadastro { Codigo = "BRMF010", Descricao = "Caixa acústica padrão 100W", UnidadeMedida = new AvalaraUnidade { Codigo = "pcs" } }, item.Item);
        Assert.Equal(new AvalaraUnidade { Codigo = "pcs" }, item.UnidadeMedida);
        Assert.Equal(6101, item.Cfop);
        Assert.Equal(10m, item.Quantidade);
        Assert.Equal(3500m, item.ValorTotal);
        Assert.Equal(4025m, item.ValorContabil);
    }

    [Fact]
    public void Numeric_cfop_outside_the_format_goes_as_is()
        => Assert.Equal(12345, MapOk(WithItem(Item() with { Cfop = "12345" })).Itens[0].Cfop);

    // ---------- tributos clássicos no bloco imposto ----------

    [Fact]
    public void Classic_taxes_go_in_the_structured_block_with_numeric_cst_base_and_amount()
    {
        AvalaraItem item = MapOk(OutgoingNote()).Itens[0];

        Assert.Equal(new AvalaraIcms { SituacaoTributariaICMSTabA = 0, SituacaoTributariaICMSTabB = 0, ValorBaseICMS = 3500m, AliquotaICMS = 12m, ValorICMS = 420m }, item.Imposto!.Icms);
        Assert.Equal(new AvalaraIpi { SituacaoTributariaIPI = 51, BaseCalculoIPI = 3500m, AliquotaIPI = 15m, ValorIPI = 525m }, item.Imposto.Ipi);
        Assert.Equal(new AvalaraPis { SituacaoTributariaPIS = 1, BaseCalculoPIS = 3500m, AliquotaPIS = 1.65m, ValorPIS = 57.75m }, item.Imposto.Pis);
        Assert.Equal(new AvalaraCofins { SituacaoTributariaCOFINS = 1, BaseCalculoCOFINS = 3500m, AliquotaCOFINS = 7.6m, ValorCOFINS = 266m }, item.Imposto.Cofins);
        Assert.Null(item.Impostos);   // nada de clássico no array genérico; o item não tem o grupo da Reforma
    }

    [Fact]
    public void Item_without_origin_goes_without_table_a()
        => Assert.Null(MapOk(WithItem(Item() with { Origin = null })).Itens[0].Imposto!.Icms!.SituacaoTributariaICMSTabA);

    [Fact]
    public void Icms_with_the_base_only_in_other_goes_with_base_zero_and_the_other_base_in_its_field()
    {
        // O ICMS CST 90 da nota gravada brmf|BRMF06-110000027: base 0, "outras" 1.000, alíquota 12, valor 0.
        GoodsInvoiceItem item = Item() with { Taxes = [new TaxLine { Kind = TaxKind.Icms, Cst = "90", TaxBase = 0m, OtherBase = 1000m, Rate = 12m, Amount = 0m }] };

        AvalaraIcms icms = MapOk(WithItem(item)).Itens[0].Imposto!.Icms!;

        Assert.Equal(0m, icms.ValorBaseICMS);
        Assert.Equal(1000m, icms.ValorBaseOutrosICMS);
        Assert.Null(icms.ValorBaseIsentoICMS);   // zero não afirmado não se escreve
        Assert.Equal(90, icms.SituacaoTributariaICMSTabB);
    }

    [Fact]
    public void Import_tax_goes_in_its_block_with_the_taxable_base_and_without_the_other_base()
    {
        // A nota de importação brmf|BRMF06-110000031, depois do complemento contábil (D9).
        GoodsInvoiceItem item = Item() with { Taxes = [new TaxLine { Kind = TaxKind.ImportTax, TaxBase = 4500m, OtherBase = 4500m, Rate = 30m, Amount = 1350m }] };

        AvalaraImpostoItem block = MapOk(WithItem(item)).Itens[0].Imposto!;

        Assert.Equal(new AvalaraIi { BaseCalculoII = 4500m, AliquotaII = 30m, ValorII = 1350m }, block.Ii);
    }

    [Fact]
    public void Icms_st_and_iss_with_its_withholding_go_in_their_blocks()
    {
        GoodsInvoiceItem item = Item() with
        {
            Taxes =
            [
                new TaxLine { Kind = TaxKind.IcmsSt, TaxBase = 200m, Rate = 18m, Amount = 36m },
                new TaxLine { Kind = TaxKind.Iss, TaxBase = 100m, Rate = 2m, Amount = 2m },
            ],
            Withholdings = [new TaxLine { Kind = TaxKind.Iss, TaxBase = 100m, Rate = 5m, Amount = 5m }],
        };

        AvalaraMapping mapping = GoodsInvoiceToAvalara.Map(WithItem(item), Header());

        AvalaraImpostoItem block = mapping.Document!.Itens[0].Imposto!;
        Assert.Equal(new AvalaraIcmsSt { ValorBaseICMSST = 200m, AliquotaICMSST = 18m, ValorICMSST = 36m }, block.Icmsst);
        Assert.Equal(
            new AvalaraIssqn { BaseCalculoISSQN = 100m, AliquotaISSQN = 2m, ValorISSQN = 2m, ValorBaseISSRetido = 100m, AliquotaISSRetido = 5m, ValorISSRetido = 5m },
            block.Issqn);
        Assert.Empty(mapping.Omissions);   // o ISS retido tem lugar próprio: não é omissão
    }

    // ---------- grupo da Reforma no array genérico ----------

    [Fact]
    public void Reform_group_becomes_three_entries_without_an_ibs_total()
    {
        List<AvalaraImposto> impostos = MapOk(WithItem(Item() with { ReformTaxes = Reform() })).Itens[0].Impostos!;

        Assert.Equal(["CBS", "IBS ESTADUAL", "IBS MUNICIPAL"], impostos.Select(i => i.Imposto.Codigo));
        Assert.All(impostos, i =>
        {
            Assert.Equal(i.Imposto, i.SituacaoTributariaImposto!.Imposto);
            Assert.Equal("000", i.SituacaoTributariaImposto.Codigo);
            Assert.Equal("000001", i.ClassificacaoTributariaImposto!.Codigo);
            Assert.Equal(2324.95m, i.ValorBaseTributo);
        });
        Assert.Equal((0.9m, 20.92m), (impostos[0].AliquotaTributo, impostos[0].ValorTributoBruto));
        Assert.Equal((0.1m, 2.32m), (impostos[1].AliquotaTributo, impostos[1].ValorTributoBruto));
        Assert.Equal((0m, 0m), (impostos[2].AliquotaTributo, impostos[2].ValorTributoBruto));
    }

    [Fact]
    public void Reform_group_without_class_trib_goes_without_classification()
        => Assert.All(
            MapOk(WithItem(Item() with { ReformTaxes = Reform() with { ClassTrib = "" } })).Itens[0].Impostos!,
            i => Assert.Null(i.ClassificacaoTributariaImposto));

    [Fact]
    public void Item_without_the_reform_group_maps_without_failing_and_without_the_array()
    {
        AvalaraItem item = MapOk(WithItem(Item() with { ReformTaxes = null })).Itens[0];

        Assert.Null(item.Impostos);
        Assert.NotNull(item.Imposto!.Icms);
    }

    // ---------- o que o contrato não consegue representar ----------

    [Fact]
    public void Unrepresentable_data_is_listed_all_at_once_and_no_document_is_built()
    {
        GoodsInvoice invoice = OutgoingNote() with
        {
            Model = "5X",
            Items =
            [
                Item() with { Cfop = "" },
                Item() with
                {
                    Number = 2,
                    Taxes = [new TaxLine { Kind = TaxKind.Pis, Cst = "XX", TaxBase = 1m, Rate = 1m, Amount = 1m }],
                },
                Item() with
                {
                    Number = 3,
                    Taxes =
                    [
                        new TaxLine { Kind = TaxKind.Icms, Cst = "00", TaxBase = 1m, Rate = 1m, Amount = 1m },
                        new TaxLine { Kind = TaxKind.Icms, Cst = "00", TaxBase = 2m, Rate = 1m, Amount = 2m },
                    ],
                },
            ],
        };

        AvalaraMapping mapping = GoodsInvoiceToAvalara.Map(invoice, Header());

        Assert.Null(mapping.Document);
        Assert.Collection(
            mapping.Problems,
            p => Assert.Contains("modelo '5X'", p),
            p => Assert.Contains("item 1: CFOP ''", p),
            p => Assert.Contains("item 2: CST 'XX' do Pis", p),
            p => Assert.Contains("item 3: Icms repetido", p));
    }

    // ---------- omissões ----------

    [Fact]
    public void What_the_contract_does_not_carry_is_left_out_and_declared()
    {
        GoodsInvoiceItem item = Item() with
        {
            Taxes = [.. Item().Taxes, new TaxLine { Kind = TaxKind.IcmsDiff, Cst = "90", TaxBase = 0m, OtherBase = 1000m, Rate = 6m, Amount = 0m }],
            Withholdings = [new TaxLine { Kind = TaxKind.Irrf, TaxBase = 1000m, Rate = 1.5m, Amount = 15m }],
            Charges = [new ItemCharge { Number = 1, Kind = ChargeKind.Other, Amount = 416.25m }],
            ReformTaxes = Reform() with { SelectiveTax = new SelectiveTax { Cst = "000", ClassTrib = "000001", TaxBase = 1m, Rate = 1m, Amount = 1m } },
        };

        AvalaraMapping mapping = GoodsInvoiceToAvalara.Map(WithItem(item), Header());

        Assert.Equal(
            [
                "item 1: IcmsDiff não enviado (sem lugar no contrato)",
                "item 1: retenção Irrf não enviada (impostosRetidos sem tradução de tipoImposto)",
                "item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)",
                "item 1: Imposto Seletivo não enviado (sem código no array impostos)",
            ],
            mapping.Omissions);
        Assert.DoesNotContain(mapping.Document!.Itens[0].Impostos!, i => i.Imposto.Codigo is "IS" or "IRRF");
    }

    [Fact]
    public void Invoice_whose_every_tax_has_a_place_declares_no_omission()
        => Assert.Empty(GoodsInvoiceToAvalara.Map(OutgoingNote(), Header()).Omissions);

    // ---------- serialização ----------

    [Fact]
    public void Serialized_payload_has_no_null_and_no_field_without_a_source()
    {
        string json = JsonSerializer.Serialize(MapOk(OutgoingNote()), AvalaraJson.Options);

        Assert.DoesNotContain("null", json);
        Assert.Contains("\"codigoEmpresa\"", json);
        Assert.Contains("\"situacaoTributariaICMSTabB\"", json);

        using JsonDocument doc = JsonDocument.Parse(json);
        string[] unsourced =
        [
            "finalidadeNotaFiscal", "operacao", "tipoPagamento", "origemSistema", "tipoItem", "origemCredito",
            "embasamentoLegal", "origemInformacao", "valorTributoLiquido", "ativo", "valorTotalComIBSCBSeIS",
            "classificacaoTributariaImposto", "impostos",
        ];
        Assert.Empty(Names(doc.RootElement).Intersect(unsourced));
        Assert.False(doc.RootElement.GetProperty("parceiro").TryGetProperty("codigo", out _));
        Assert.Equal(["valorMercadorias", "valorDocumento"], doc.RootElement.GetProperty("totais").EnumerateObject().Select(p => p.Name));
    }

    // ---------- apoio ----------

    private static AvalaraDocument MapOk(GoodsInvoice invoice)
    {
        AvalaraMapping mapping = GoodsInvoiceToAvalara.Map(invoice, Header());
        Assert.Empty(mapping.Problems);
        return mapping.Document!;
    }

    private static AvalaraHeaderData Header() => new(Codes, Southridge(), Key);

    private static IEnumerable<string> Names(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Names(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Names),
        _ => [],
    };

    private static Party Southridge() => new()
    {
        TaxId = "72458488000106",
        Name = "Southridge Video Brasil Ltda",
        StateRegistration = "55166625",
        MunicipalityCode = "3304557",
        Address = new Address { Street = "Estrada do Galeão", Number = "135", District = "Ilha do Governador", PostalCode = "21931-385" },
    };

    private static GoodsInvoice WithItem(GoodsInvoiceItem item) => OutgoingNote() with { Items = [item] };

    // O domínio da nota gravada brmf|BRMF21-10000026, como a montagem do D365 o entrega (só o item 1).
    private static GoodsInvoice OutgoingNote() => new()
    {
        AccessKey = "35130344278225000180550020000000011000190380",
        Model = "55",
        Series = "02",
        Number = "000001",
        IssueDate = new DateTimeOffset(2016, 3, 1, 12, 0, 0, TimeSpan.Zero),
        EntryExitDate = new DateTimeOffset(2016, 3, 1, 12, 0, 0, TimeSpan.Zero),
        Issuance = Issuance.Own,
        Issuer = new Party { TaxId = "44278225000180", Name = "Contoso Entertainment System Brazil" },
        Recipient = Southridge(),
        TotalAmount = 12600m,
        GoodsAmount = 11250m,
        Items = [Item()],
    };

    private static GoodsInvoiceItem Item() => new()
    {
        Number = 1,
        ProductCode = "BRMF010",
        Description = "Caixa acústica padrão 100W",
        Ncm = "85182200",
        Cfop = "6101",
        Quantity = 10m,
        UnitAmount = 350m,
        TotalAmount = 3500m,
        Unit = "pcs",
        AccountingAmount = 4025m,
        Origin = "0",
        Taxes =
        [
            new TaxLine { Kind = TaxKind.Cofins, Cst = "01", TaxBase = 3500m, Rate = 7.6m, Amount = 266m },
            new TaxLine { Kind = TaxKind.Icms, Cst = "00", TaxBase = 3500m, Rate = 12m, Amount = 420m },
            new TaxLine { Kind = TaxKind.Ipi, Cst = "51", TaxBase = 3500m, Rate = 15m, Amount = 525m },
            new TaxLine { Kind = TaxKind.Pis, Cst = "01", TaxBase = 3500m, Rate = 1.65m, Amount = 57.75m },
        ],
    };

    // O grupo do JSON real de mercadoria da Avalara.
    private static ReformTaxes Reform() => new()
    {
        Cst = "000",
        ClassTrib = "000001",
        TaxBase = 2324.95m,
        IbsCbs = new IbsCbs
        {
            IbsState = new TaxShare { Rate = 0.1m, Amount = 2.32m },
            IbsMunicipality = new TaxShare { Rate = 0m, Amount = 0m },
            IbsTotalAmount = 2.32m,
            Cbs = new TaxShare { Rate = 0.9m, Amount = 20.92m },
        },
    };
}
