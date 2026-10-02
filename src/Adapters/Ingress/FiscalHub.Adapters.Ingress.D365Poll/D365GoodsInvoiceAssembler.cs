using System.Globalization;
using System.Text.Json;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>Município (código IBGE) e endereço de uma parte, já resolvidos pelo cache de cadastros.</summary>
internal sealed record D365PartyPlace(string? MunicipalityCode, Address? Address)
{
    public static readonly D365PartyPlace None = new(null, null);

    /// <summary>Endereço da linha de <c>FSPostalAddressBRs</c>; campo vazio fica nulo, e sem nenhum campo não há endereço.</summary>
    public static Address? AddressOf(JsonElement row)
    {
        var address = new Address
        {
            Street = Text(row, "Street"),
            Number = Text(row, "StreetNumber"),
            District = Text(row, "DistrictName"),
            PostalCode = Text(row, "ZipCode"),
        };
        return address is { Street: null, Number: null, District: null, PostalCode: null } ? null : address;

        static string? Text(JsonElement row, string name)
            => row.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
    }
}

/// <summary>Cadastros das duas partes do cabeçalho: o estabelecimento e o terceiro.</summary>
internal sealed record D365PartyReferenceData(D365PartyPlace Establishment, D365PartyPlace ThirdParty);

/// <summary>
/// Monta a <see cref="GoodsInvoice"/> a partir das respostas do D365 — função pura, sem rede (ADR-0025, design D7 a
/// D10 e D14). A entidade fiscal de impostos é a fonte: tipo, CST, base, alíquota, valor e lugar no documento vêm
/// dela, sem regra de sinal nem de CST. Cada imposto cai num lugar só, pela chave estrangeira. O complemento contábil
/// vale só para os tipos em que se sabe que a fiscal vem zerada, campo a campo. O que fugir do padrão conhecido falha
/// com <see cref="D365AssemblyException"/>, em vez de escolher um lado.
/// </summary>
internal static class D365GoodsInvoiceAssembler
{
    /// <summary>
    /// Tipos cuja entidade fiscal vem zerada e se completam pela contábil (ponte <c>TaxTransRecId</c>). Comportamento do
    /// produto F&amp;O, igual para todo cliente — não é setting de tenant. Outro tipo entra só com evidência nova e nota
    /// no ADR-0025. O IPI CST 05 zerado, por exemplo, NÃO entra: zero é coerente com suspensão.
    /// </summary>
    private static readonly HashSet<string> ComplementTypes = new(StringComparer.Ordinal) { "ImportTax" };

    private static readonly HashSet<string> ReformTypes = new(StringComparer.Ordinal) { "CBS", "IBSState", "IBSCity" };

    // Um para um com o TaxType_BR, sem fusão. Blank (e qualquer valor fora daqui) falha.
    private static readonly Dictionary<string, TaxKind> TaxKinds = new(StringComparer.Ordinal)
    {
        ["ICMS"] = TaxKind.Icms,
        ["ICMSST"] = TaxKind.IcmsSt,
        ["ICMSDiff"] = TaxKind.IcmsDiff,
        ["IPI"] = TaxKind.Ipi,
        ["PIS"] = TaxKind.Pis,
        ["COFINS"] = TaxKind.Cofins,
        ["ImportTax"] = TaxKind.ImportTax,
        ["ISS"] = TaxKind.Iss,
        ["IRRF"] = TaxKind.Irrf,
        ["INSS"] = TaxKind.Inss,
        ["INSSRetained"] = TaxKind.InssRetained,
        ["INSSCPRB"] = TaxKind.InssCprb,
        ["CSLL"] = TaxKind.Csll,
        ["OtherTax"] = TaxKind.Other,
    };

    // Só o que a base mostrou. Frete e seguro entram quando houver nota com eles (nomes do enum a confirmar).
    private static readonly Dictionary<string, ChargeKind> ChargeKinds = new(StringComparer.Ordinal)
    {
        ["Others"] = ChargeKind.Other,
    };

    // Origem da mercadoria → dígito da tabela de origem do leiaute (a Tabela A do CST do ICMS). Só os nomes que a base
    // mostrou (d365/04 §3.3); outro nome deixa a origem ausente, e nunca 0 — cada valor novo entra com evidência.
    private static readonly Dictionary<string, string> Origins = new(StringComparer.Ordinal)
    {
        ["National"] = "0",
        ["DirectImport"] = "1",
    };

    /// <summary>A nota tem imposto elegível ao complemento? Se sim, o source busca a contábil do voucher (uma chamada a mais).</summary>
    public static bool NeedsAccounting(IReadOnlyList<JsonElement> taxes) => taxes.Any(IsComplementEligible);

    /// <summary>
    /// Pontes (<c>TaxTransRecId</c>) dos impostos elegíveis ao complemento: só as linhas contábeis com essas chaves
    /// entram no canônico e na montagem — o resto do voucher não afeta a impressão.
    /// </summary>
    public static IReadOnlySet<long> ComplementBridges(IReadOnlyList<JsonElement> taxes)
        => taxes.Where(IsComplementEligible).Select(t => Long(t, "TaxTransRecId")).ToHashSet();

    public static GoodsInvoice Assemble(D365DocumentRows rows, D365PartyReferenceData referenceData)
    {
        JsonElement header = rows.Header;
        string voucher = Str(header, "Voucher");

        var lines = new Dictionary<long, LineDraft>();
        foreach (JsonElement line in rows.Lines)
        {
            lines[Long(line, "FiscalDocumentLineRecId")] = new LineDraft(line);
        }

        var charges = new Dictionary<long, ChargeDraft>();
        foreach (JsonElement charge in rows.Charges)
        {
            long chargeRecId = Long(charge, "FiscalDocumentMiscChargeRecId");
            long lineRecId = Long(charge, "FiscalDocumentLineRecId");
            if (!lines.TryGetValue(lineRecId, out LineDraft? owner))
            {
                throw new D365AssemblyException($"Documento {voucher}: encargo {chargeRecId} aponta para a linha {lineRecId}, fora do documento.");
            }

            string type = Str(charge, "MiscChargeType");
            if (!ChargeKinds.TryGetValue(type, out ChargeKind kind))
            {
                throw new D365AssemblyException($"Documento {voucher}: encargo {chargeRecId} com tipo '{type}' sem mapeamento (a base só mostrou 'Others').");
            }

            var draft = new ChargeDraft(charge, kind);
            charges[chargeRecId] = draft;
            owner.Charges.Add(draft);
        }

        foreach (JsonElement tax in rows.Taxes)
        {
            Place(tax, lines, charges, rows.Accounting, voucher);
        }

        (Party establishment, Party thirdParty) = Parties(header, referenceData);
        bool ownIssued = Str(header, "FiscalDocumentIssuer") switch
        {
            "OwnEstablishment" => true,
            "ThirdParty" => false,
            string other => throw new D365AssemblyException($"Documento {voucher}: FiscalDocumentIssuer '{other}' desconhecido."),
        };

        return new GoodsInvoice
        {
            AccessKey = Str(header, "AccessKey"),
            Model = Str(header, "Model"),
            Series = Str(header, "FiscalDocumentSeries"),
            Number = Str(header, "FiscalDocumentNumber"),
            IssueDate = IssueDate(header),
            FiscalDate = D365HeaderValues.FiscalDay(Str(header, "FiscalDocumentDate")),   // o dia, sem conversão de fuso
            EntryExitDate = OptionalDate(header, "AccountingDate"),
            Issuance = ownIssued ? Issuance.Own : Issuance.ThirdParty,
            // O estabelecimento que escritura, em qualquer direção: o mesmo campo que monta a parte dele.
            Establishment = new Establishment { TaxId = establishment.TaxId, Code = Str(header, "FiscalEstablishment") },
            Issuer = ownIssued ? establishment : thirdParty,
            Recipient = ownIssued ? thirdParty : establishment,
            Items = [.. lines.Values.OrderBy(l => Dec(l.Row, "LineNum")).Select(l => Item(l, voucher))],
            TotalAmount = Dec(header, "TotalAmount"),
            GoodsAmount = Dec(header, "TotalGoodsAmount"),
        };
    }

    private static void Place(
        JsonElement tax, Dictionary<long, LineDraft> lines, Dictionary<long, ChargeDraft> charges, IReadOnlyList<JsonElement> accounting, string voucher)
    {
        long recId = Long(tax, "FiscalDocumentTaxTransRecId");
        string type = Str(tax, "FiscalTaxType");
        long lineKey = Long(tax, "FiscalDocumentLineRecId");
        long chargeKey = Long(tax, "FiscalDocumentMiscChargeRecId");
        bool retained = Str(tax, "RetainedTax") == "Yes";

        if (lineKey == 0 && chargeKey == 0)
        {
            throw new D365AssemblyException($"Documento {voucher}: imposto {recId} ({type}) sem linha nem encargo.");
        }

        if (lineKey != 0 && chargeKey != 0)
        {
            throw new D365AssemblyException($"Documento {voucher}: imposto {recId} ({type}) aponta para linha e encargo ao mesmo tempo ({lineKey}, {chargeKey}).");
        }

        TaxTarget target;
        if (lineKey != 0)
        {
            target = lines.TryGetValue(lineKey, out LineDraft? line)
                ? line
                : throw new D365AssemblyException($"Documento {voucher}: imposto {recId} ({type}) aponta para a linha {lineKey}, fora do documento.");
        }
        else
        {
            target = charges.TryGetValue(chargeKey, out ChargeDraft? charge)
                ? charge
                : throw new D365AssemblyException($"Documento {voucher}: imposto {recId} ({type}) aponta para o encargo {chargeKey}, fora do documento.");
        }

        if (ReformTypes.Contains(type))
        {
            if (target is not LineDraft reformLine || retained)
            {
                throw new D365AssemblyException($"Documento {voucher}: imposto {recId} ({type}) retido ou em encargo — caso do grupo IBS/CBS não previsto.");
            }

            reformLine.Reform.Add(tax);
            return;
        }

        if (!TaxKinds.TryGetValue(type, out TaxKind kind))
        {
            throw new D365AssemblyException($"Documento {voucher}: imposto {recId} com tipo '{type}' sem mapeamento.");
        }

        TaxLine taxLine = ToTaxLine(tax, kind, accounting, voucher);
        (retained ? target.Withholdings : target.Taxes).Add(taxLine);
    }

    private static TaxLine ToTaxLine(JsonElement tax, TaxKind kind, IReadOnlyList<JsonElement> accounting, string voucher)
    {
        decimal taxBase = Dec(tax, "TaxBaseAmount");
        decimal rate = Dec(tax, "TaxValue");
        decimal amount = Dec(tax, "TaxAmount");

        if (IsComplementEligible(tax))
        {
            long recId = Long(tax, "FiscalDocumentTaxTransRecId");
            string type = Str(tax, "FiscalTaxType");
            long bridge = Long(tax, "TaxTransRecId");
            JsonElement? match = accounting.Where(a => Long(a, "TaxTransRecId") == bridge).Select(a => (JsonElement?)a).FirstOrDefault();
            if (match is not { } acc)
            {
                throw new D365AssemblyException(
                    $"Documento {voucher}: imposto {recId} ({type}) zerado na fiscal com ponte {bridge}, sem linha contábil correspondente no voucher.");
            }

            string accountingType = Str(acc, "TaxType");
            if (accountingType != type)
            {
                throw new D365AssemblyException(
                    $"Documento {voucher}: imposto {recId} ({type}) e linha contábil {bridge} ({accountingType}) com tipos diferentes.");
            }

            decimal accBase = Dec(acc, "TaxBaseAmount"), accRate = Dec(acc, "TaxValue"), accAmount = Dec(acc, "TaxAmount");
            if (accBase < 0 || accRate < 0 || accAmount < 0)
            {
                throw new D365AssemblyException(
                    $"Documento {voucher}: linha contábil {bridge} do imposto {recId} com valor negativo (base {F(accBase)}, alíquota {F(accRate)}, valor {F(accAmount)}); o complemento só aceita valor não negativo.");
            }

            // Campo a campo: vale a fiscal quando diferente de zero; zero na fiscal, vale a contábil.
            taxBase = Merge("TaxBaseAmount", taxBase, accBase);
            rate = Merge("TaxValue", rate, accRate);
            amount = Merge("TaxAmount", amount, accAmount);

            decimal Merge(string field, decimal fiscal, decimal contabil)
            {
                if (fiscal == 0)
                {
                    return contabil;
                }

                return contabil == 0 || contabil == fiscal
                    ? fiscal
                    : throw new D365AssemblyException(
                        $"Documento {voucher}: imposto {recId} e linha contábil {bridge} divergem em {field}: fiscal {F(fiscal)}, contábil {F(contabil)}.");
            }
        }

        return new TaxLine
        {
            Kind = kind,
            Cst = NullIfEmpty(Str(tax, "TaxationCode")),
            TaxBase = taxBase,
            Rate = rate,
            Amount = amount,
            ExemptBase = Dec(tax, "TaxBaseAmountExempt"),
            OtherBase = Dec(tax, "TaxBaseAmountOther"),
        };
    }

    private static GoodsInvoiceItem Item(LineDraft line, string voucher)
    {
        JsonElement row = line.Row;
        decimal lineNum = Dec(row, "LineNum");
        if (lineNum != decimal.Truncate(lineNum))
        {
            throw new D365AssemblyException($"Documento {voucher}: linha {Long(row, "FiscalDocumentLineRecId")} com LineNum fracionário ({F(lineNum)}).");
        }

        return new GoodsInvoiceItem
        {
            Number = (int)lineNum,
            ProductCode = Str(row, "ItemId"),
            Description = Str(row, "Description"),
            Ncm = Digits(Str(row, "FiscalClassification")),
            Cfop = Digits(Str(row, "CFOP")),
            Quantity = Dec(row, "Quantity"),
            UnitAmount = Dec(row, "UnitPrice"),
            TotalAmount = Dec(row, "LineAmount"),
            Unit = NullIfEmpty(Str(row, "Unit")),
            AccountingAmount = Dec(row, "AccountingAmount"),
            Origin = OriginOf(Str(row, "Origin")),
            ReformTaxes = ReformGroup(line, (int)lineNum, voucher),
            Taxes = line.Taxes,
            Withholdings = line.Withholdings,
            Charges = [.. line.Charges.Select(c => c.ToCharge())],
        };
    }

    /// <summary>Nenhum dos três tipos → grupo ausente (não zerado). Os três, uma vez cada → grupo. Parcial ou divergente → falha.</summary>
    private static ReformTaxes? ReformGroup(LineDraft line, int lineNum, string voucher)
    {
        if (line.Reform.Count == 0)
        {
            return null;
        }

        var byType = line.Reform.GroupBy(t => Str(t, "FiscalTaxType")).ToDictionary(g => g.Key, g => g.ToList());
        if (!ReformTypes.All(t => byType.TryGetValue(t, out List<JsonElement>? found) && found.Count == 1))
        {
            throw new D365AssemblyException(
                $"Documento {voucher}, item {lineNum}: grupo IBS/CBS incompleto ({string.Join(", ", line.Reform.Select(t => Str(t, "FiscalTaxType")))}); precisa de CBS, IBSState e IBSCity, uma vez cada.");
        }

        JsonElement cbs = byType["CBS"][0], state = byType["IBSState"][0], city = byType["IBSCity"][0];
        JsonElement[] three = [cbs, state, city];
        if (three.Select(t => Str(t, "TaxationCode")).Distinct().Count() > 1 || three.Select(t => Dec(t, "TaxBaseAmount")).Distinct().Count() > 1)
        {
            throw new D365AssemblyException($"Documento {voucher}, item {lineNum}: CST ou base diferentes entre CBS, IBSState e IBSCity.");
        }

        return new ReformTaxes
        {
            Cst = Str(cbs, "TaxationCode"),
            ClassTrib = string.Empty,   // a entidade fiscal não traz o cClassTrib; na contábil é RecId sem entidade (ADR-0025 §6)
            TaxBase = Dec(cbs, "TaxBaseAmount"),
            IbsCbs = new IbsCbs
            {
                IbsState = Share(state),
                IbsMunicipality = Share(city),
                IbsTotalAmount = Dec(state, "TaxAmount") + Dec(city, "TaxAmount"),   // vIBS = vIBSUF + vIBSMun (leiaute)
                Cbs = Share(cbs),
            },
        };

        static TaxShare Share(JsonElement t) => new() { Rate = Dec(t, "TaxValue"), Amount = Dec(t, "TaxAmount") };
    }

    private static (Party Establishment, Party ThirdParty) Parties(JsonElement header, D365PartyReferenceData referenceData)
        => (Party(header, "FiscalEstablishment", referenceData.Establishment), Party(header, "ThirdParty", referenceData.ThirdParty));

    private static Party Party(JsonElement header, string prefix, D365PartyPlace place) => new()
    {
        TaxId = D365HeaderValues.TaxId(Str(header, $"{prefix}CNPJCPF")),
        Name = Str(header, $"{prefix}Name"),
        StateRegistration = NullIfEmpty(Str(header, $"{prefix}IE")),
        MunicipalityCode = place.MunicipalityCode,
        Address = place.Address,
    };

    /// <summary>O F&amp;O devolve 1900-01-01 como data vazia; aí vale o <c>FiscalDocumentDate</c>, como veio (12:00 UTC).</summary>
    private static DateTimeOffset IssueDate(JsonElement header)
    {
        DateTimeOffset dateTime = Date(header, "FiscalDocumentDateTime");
        return dateTime.Year > 1900 ? dateTime : Date(header, "FiscalDocumentDate");
    }

    /// <summary>Nome de enum com tradução (d365/04 §3.3), ou já o dígito de 0 a 8; qualquer outro valor → ausente.</summary>
    private static string? OriginOf(string value)
        => Origins.TryGetValue(value, out string? digit) ? digit : value is [>= '0' and <= '8'] ? value : null;

    /// <summary>Data que pode vir vazia (1900-01-01 no F&amp;O): vazia fica ausente.</summary>
    private static DateTimeOffset? OptionalDate(JsonElement header, string name)
    {
        DateTimeOffset date = Date(header, name);
        return date.Year > 1900 ? date : null;
    }

    private static bool IsComplementEligible(JsonElement tax)
        => ComplementTypes.Contains(Str(tax, "FiscalTaxType")) && Dec(tax, "TaxAmount") == 0 && Long(tax, "TaxTransRecId") != 0;

    // ---------- leitura do JSON: campo ausente é quebra de contrato ($select), nunca default silencioso ----------

    private static JsonElement Field(JsonElement row, string name)
        => row.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new D365AssemblyException($"Campo '{name}' ausente na resposta do F&O (o $select mudou?).");

    private static string Str(JsonElement row, string name) => Field(row, name) is { ValueKind: JsonValueKind.String } s ? s.GetString()! : string.Empty;

    /// <summary>Chave (RecId/FK): nulo vale 0, que no F&amp;O é "vazio".</summary>
    private static long Long(JsonElement row, string name) => Field(row, name) is { ValueKind: JsonValueKind.Number } n ? n.GetInt64() : 0;

    private static decimal Dec(JsonElement row, string name)
        => Field(row, name) is { ValueKind: JsonValueKind.Number } n
            ? n.GetDecimal()
            : throw new D365AssemblyException($"Campo '{name}' sem valor numérico na resposta do F&O.");

    private static DateTimeOffset Date(JsonElement row, string name)
        => DateTimeOffset.Parse(Str(row, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    // Só para o NCM e o CFOP, que são códigos numéricos. O CNPJ e o CPF vão pelo D365HeaderValues.TaxId.
    private static string Digits(string value) => D365HeaderValues.Digits(value);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static string F(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private abstract class TaxTarget
    {
        public List<TaxLine> Taxes { get; } = [];

        public List<TaxLine> Withholdings { get; } = [];
    }

    private sealed class LineDraft(JsonElement row) : TaxTarget
    {
        public JsonElement Row { get; } = row;

        public List<ChargeDraft> Charges { get; } = [];

        public List<JsonElement> Reform { get; } = [];
    }

    private sealed class ChargeDraft(JsonElement row, ChargeKind kind) : TaxTarget
    {
        public ItemCharge ToCharge() => new()
        {
            Number = (int)Dec(row, "ChargeNum"),
            Kind = kind,
            Amount = Dec(row, "Amount"),
            Description = NullIfEmpty(Str(row, "Txt")),
            Taxes = Taxes,
            Withholdings = Withholdings,
        };
    }
}
