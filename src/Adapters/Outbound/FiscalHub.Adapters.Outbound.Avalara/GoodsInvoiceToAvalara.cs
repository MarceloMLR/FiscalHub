using System.Globalization;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>O que o mapeamento precisa além do documento: os códigos da plataforma, o parceiro e a referência do hub.</summary>
internal sealed record AvalaraHeaderData(AvalaraCompanyCodes Codes, Party Partner, string ReferenceKey);

/// <summary>
/// Resultado do mapeamento: o payload (nulo quando há problema de contrato), os problemas — dado que o contrato não
/// consegue representar — e as omissões — o que o documento tem e o contrato não leva.
/// </summary>
internal sealed record AvalaraMapping(AvalaraDocument? Document, IReadOnlyList<string> Problems, IReadOnlyList<string> Omissions);

/// <summary>
/// Mapeia a <see cref="GoodsInvoice"/> do domínio para o contrato da Avalara (design D3 e D6 a D9). Não julga conteúdo
/// fiscal (ADR-0026): emite o que tem. O contrato vem dos JSONs reais — clássicos no bloco <c>imposto</c>, o array
/// <c>impostos</c> só para o grupo da Reforma — e, onde eles são silenciosos, dos nomes do schema completo.
/// </summary>
internal static class GoodsInvoiceToAvalara
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static AvalaraMapping Map(GoodsInvoice invoice, AvalaraHeaderData header)
    {
        var problems = new List<string>();
        var omissions = new List<string>();

        int? model = Number(invoice.Model);
        if (model is null)
        {
            problems.Add($"modelo '{invoice.Model}' não é número (o contrato leva modelo numérico)");
        }

        List<AvalaraItem?> items = [.. invoice.Items.Select(item => MapItem(item, problems, omissions))];
        if (problems.Count > 0)
        {
            return new AvalaraMapping(null, problems, omissions);
        }

        var document = new AvalaraDocument
        {
            CodigoEmpresa = header.Codes.CodigoEmpresa,
            CodigoContribuinte = header.Codes.CodigoContribuinte,
            Serie = NullIfEmpty(invoice.Series),
            Modelo = model!.Value,
            NumeroDocumento = invoice.Number,
            ChaveNFe = NullIfEmpty(invoice.AccessKey),
            DataEmissao = invoice.IssueDate.UtcDateTime,
            DataEntradaSaida = invoice.EntryExitDate?.UtcDateTime,
            PeriodoEscrituracao = invoice.EntryExitDate is { } entry ? new DateTime(entry.Year, entry.Month, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            Situacao = 0,   // só nota autorizada é despachada, e como emitida: documento regular
            CodigoReferenciaIntegracao = header.ReferenceKey,
            Parceiro = Partner(header.Partner),
            Itens = [.. items.Select(i => i!)],
            Totais = new AvalaraTotais { ValorMercadorias = invoice.GoodsAmount, ValorDocumento = invoice.TotalAmount },
        };

        return new AvalaraMapping(document, problems, omissions);
    }

    private static AvalaraParceiro Partner(Party party)
    {
        // Sem a pontuação e com as letras: o CNPJ alfanumérico tem 14 caracteres e vai em cnpj (tax-identifier-normalization).
        string taxId = TaxIdentifiers.Normalize(party.TaxId);
        return new AvalaraParceiro
        {
            Nome = party.Name,
            Cnpj = taxId.Length == 14 ? taxId : null,
            Cpf = taxId.Length == 11 ? taxId : null,
            Endereco = party.Address?.Street,
            Numero = party.Address?.Number,
            Bairro = party.Address?.District,
            Cep = party.Address?.PostalCode is { } cep ? NullIfEmpty(Digits(cep)) : null,
        };
    }

    private static AvalaraItem? MapItem(GoodsInvoiceItem item, List<string> problems, List<string> omissions)
    {
        int? cfop = Number(item.Cfop);
        if (cfop is null)
        {
            problems.Add($"item {item.Number}: CFOP '{item.Cfop}' não é número (o contrato leva cfop numérico)");
        }

        AvalaraImpostoItem? block = Block(item, problems, omissions);

        foreach (ItemCharge charge in item.Charges)
        {
            string tributes = charge.Taxes.Count + charge.Withholdings.Count is var n and > 0 ? $", com {n} tributo(s)," : string.Empty;
            omissions.Add($"item {item.Number}: encargo {charge.Kind}{tributes} de {charge.Amount.ToString("N2", PtBr)} não enviado (o contrato mínimo não tem campo de encargo)");
        }

        if (item.ReformTaxes?.SelectiveTax is not null)
        {
            omissions.Add($"item {item.Number}: Imposto Seletivo não enviado (sem código no array impostos)");
        }

        if (cfop is null)
        {
            return null;
        }

        AvalaraUnidade? unit = item.Unit is { Length: > 0 } u ? new AvalaraUnidade { Codigo = u } : null;
        return new AvalaraItem
        {
            NumeroSequencia = item.Number,
            Item = new AvalaraItemCadastro { Codigo = item.ProductCode, Descricao = item.Description, UnidadeMedida = unit },
            Cfop = cfop.Value,
            UnidadeMedida = unit,
            Quantidade = item.Quantity,
            ValorTotal = item.TotalAmount,
            ValorContabil = item.AccountingAmount,
            Imposto = block,
            Impostos = item.ReformTaxes is { } reform ? ReformEntries(reform) : null,
        };
    }

    // Um bloco por tributo, como os JSONs reais mandam (ICMS, PIS, COFINS, ISSQN) e o schema nomeia (IPI, II, ICMS-ST,
    // ISS retido). Tributo sem lugar no contrato vira omissão; dois do mesmo bloco, problema — somar ou escolher seria
    // julgamento.
    private static AvalaraImpostoItem? Block(GoodsInvoiceItem item, List<string> problems, List<string> omissions)
    {
        AvalaraIcms? icms = null;
        AvalaraIpi? ipi = null;
        AvalaraPis? pis = null;
        AvalaraCofins? cofins = null;
        AvalaraIi? ii = null;
        AvalaraIcmsSt? icmsSt = null;
        AvalaraIssqn? issqn = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        bool First(TaxKind kind, bool retained = false)
        {
            string key = retained ? $"{kind}-retido" : kind.ToString();
            if (seen.Add(key))
            {
                return true;
            }

            problems.Add($"item {item.Number}: {key} repetido (o contrato leva um bloco por tributo)");
            return false;
        }

        foreach (TaxLine tax in item.Taxes)
        {
            switch (tax.Kind)
            {
                case TaxKind.Icms when First(tax.Kind):
                    icms = new AvalaraIcms
                    {
                        SituacaoTributariaICMSTabA = item.Origin is { } origin ? Number(origin) : null,
                        SituacaoTributariaICMSTabB = Cst(item, tax, problems),
                        ValorBaseICMS = tax.TaxBase,
                        AliquotaICMS = tax.Rate,
                        ValorICMS = tax.Amount,
                        ValorBaseIsentoICMS = NonZero(tax.ExemptBase),
                        ValorBaseOutrosICMS = NonZero(tax.OtherBase),
                    };
                    break;

                case TaxKind.Ipi when First(tax.Kind):
                    ipi = new AvalaraIpi
                    {
                        SituacaoTributariaIPI = Cst(item, tax, problems),
                        BaseCalculoIPI = tax.TaxBase,
                        AliquotaIPI = tax.Rate,
                        ValorIPI = tax.Amount,
                        ValorBaseIsentoIPI = NonZero(tax.ExemptBase),
                        ValorBaseOutrosIPI = NonZero(tax.OtherBase),
                    };
                    break;

                case TaxKind.Pis when First(tax.Kind):
                    pis = new AvalaraPis { SituacaoTributariaPIS = Cst(item, tax, problems), BaseCalculoPIS = tax.TaxBase, AliquotaPIS = tax.Rate, ValorPIS = tax.Amount };
                    break;

                case TaxKind.Cofins when First(tax.Kind):
                    cofins = new AvalaraCofins { SituacaoTributariaCOFINS = Cst(item, tax, problems), BaseCalculoCOFINS = tax.TaxBase, AliquotaCOFINS = tax.Rate, ValorCOFINS = tax.Amount };
                    break;

                case TaxKind.ImportTax when First(tax.Kind):
                    ii = new AvalaraIi { BaseCalculoII = tax.TaxBase, AliquotaII = tax.Rate, ValorII = tax.Amount };
                    break;

                case TaxKind.IcmsSt when First(tax.Kind):
                    icmsSt = new AvalaraIcmsSt { ValorBaseICMSST = tax.TaxBase, AliquotaICMSST = tax.Rate, ValorICMSST = tax.Amount };
                    break;

                case TaxKind.Iss when First(tax.Kind):
                    issqn = (issqn ?? new AvalaraIssqn()) with { BaseCalculoISSQN = tax.TaxBase, AliquotaISSQN = tax.Rate, ValorISSQN = tax.Amount };
                    break;

                case TaxKind.Icms or TaxKind.Ipi or TaxKind.Pis or TaxKind.Cofins or TaxKind.ImportTax or TaxKind.IcmsSt or TaxKind.Iss:
                    break;   // repetido: o problema já foi registrado

                default:
                    omissions.Add($"item {item.Number}: {tax.Kind} não enviado (sem lugar no contrato)");
                    break;
            }
        }

        foreach (TaxLine withholding in item.Withholdings)
        {
            if (withholding.Kind == TaxKind.Iss)
            {
                if (First(TaxKind.Iss, retained: true))
                {
                    issqn = (issqn ?? new AvalaraIssqn()) with
                    {
                        ValorBaseISSRetido = withholding.TaxBase,
                        AliquotaISSRetido = withholding.Rate,
                        ValorISSRetido = withholding.Amount,
                    };
                }
            }
            else
            {
                omissions.Add($"item {item.Number}: retenção {withholding.Kind} não enviada (impostosRetidos sem tradução de tipoImposto)");
            }
        }

        var block = new AvalaraImpostoItem { Icms = icms, Ipi = ipi, Pis = pis, Cofins = cofins, Ii = ii, Icmsst = icmsSt, Issqn = issqn };
        return block == new AvalaraImpostoItem() ? null : block;
    }

    // O grupo IBS/CBS vira três entradas no array genérico, como no JSON real: sem entrada de total do IBS.
    private static List<AvalaraImposto> ReformEntries(ReformTaxes r)
        => [Entry("CBS", r, r.IbsCbs.Cbs), Entry("IBS ESTADUAL", r, r.IbsCbs.IbsState), Entry("IBS MUNICIPAL", r, r.IbsCbs.IbsMunicipality)];

    private static AvalaraImposto Entry(string code, ReformTaxes r, TaxShare share)
    {
        var imposto = new AvalaraCodigo { Codigo = code };
        return new AvalaraImposto
        {
            Imposto = imposto,
            SituacaoTributariaImposto = r.Cst is { Length: > 0 } cst ? new AvalaraCodigoDeImposto { Imposto = imposto, Codigo = cst } : null,
            ClassificacaoTributariaImposto = r.ClassTrib is { Length: > 0 } classTrib ? new AvalaraCodigoDeImposto { Imposto = imposto, Codigo = classTrib } : null,
            ValorBaseTributo = r.TaxBase,
            AliquotaTributo = share.Rate,
            ValorTributoBruto = share.Amount,
        };
    }

    // CST do bloco é inteiro no contrato: "01" → 1; vazio → ausente; não numérico → problema de contrato.
    private static int? Cst(GoodsInvoiceItem item, TaxLine tax, List<string> problems)
    {
        if (string.IsNullOrEmpty(tax.Cst))
        {
            return null;
        }

        int? cst = Number(tax.Cst);
        if (cst is null)
        {
            problems.Add($"item {item.Number}: CST '{tax.Cst}' do {tax.Kind} não é número (o contrato leva CST numérico)");
        }

        return cst;
    }

    // Base isenta e em "outras": zero é a ausência da parcela — e o TaxLine guarda zero por padrão, então zero não se
    // escreve (nunca afirmar o que o documento não afirmou).
    private static decimal? NonZero(decimal value) => value == 0m ? null : value;

    private static int? Number(string value)
        => value.Length > 0 && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;

    // Só para o CEP. O CNPJ e o CPF vão pela TaxIdentifiers.Normalize.
    private static string Digits(string value) => new([.. value.Where(char.IsAsciiDigit)]);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
