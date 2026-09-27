namespace FiscalHub.Adapters.Outbound.Avalara;

// DTOs do contrato da Avalara para a NF-e 55, a partir do JSON REAL de mercadoria (design D3) — e, onde ele é
// silencioso, dos nomes do schema completo em docs/contracts (D6). São internal: o formato externo fica preso no
// adapter (camada anticorrupção). Campo opcional é anulável e não é escrito quando nulo (WhenWritingNull): valor que o
// documento não tem fica fora do payload — nunca zero, nulo explícito ou texto de preenchimento.

internal sealed record AvalaraDocument
{
    public required string CodigoEmpresa { get; init; }
    public required string CodigoContribuinte { get; init; }
    public string? Serie { get; init; }
    public required int Modelo { get; init; }
    public required string NumeroDocumento { get; init; }
    public string? ChaveNFe { get; init; }
    public required DateTime DataEmissao { get; init; }
    public DateTime? DataEntradaSaida { get; init; }
    public DateTime? PeriodoEscrituracao { get; init; }

    /// <summary>0 = documento regular: só nota autorizada é despachada, como emitida.</summary>
    public required int Situacao { get; init; }

    public required string CodigoReferenciaIntegracao { get; init; }
    public required AvalaraParceiro Parceiro { get; init; }
    public required List<AvalaraItem> Itens { get; init; }
    public required AvalaraTotais Totais { get; init; }
}

internal sealed record AvalaraParceiro
{
    public required string Nome { get; init; }
    public string? Cnpj { get; init; }
    public string? Cpf { get; init; }
    public string? Endereco { get; init; }
    public string? Numero { get; init; }
    public string? Bairro { get; init; }
    public string? Cep { get; init; }
}

internal sealed record AvalaraItem
{
    public required int NumeroSequencia { get; init; }
    public required AvalaraItemCadastro Item { get; init; }
    public required int Cfop { get; init; }
    public AvalaraUnidade? UnidadeMedida { get; init; }
    public required decimal Quantidade { get; init; }
    public required decimal ValorTotal { get; init; }
    public decimal? ValorContabil { get; init; }

    /// <summary>Tributos clássicos, um bloco por tributo (como os JSONs reais mandam).</summary>
    public AvalaraImpostoItem? Imposto { get; init; }

    /// <summary>Só o grupo da Reforma (CBS, IBS ESTADUAL, IBS MUNICIPAL); ausente quando o item não tem o grupo.</summary>
    public List<AvalaraImposto>? Impostos { get; init; }
}

internal sealed record AvalaraItemCadastro
{
    public required string Codigo { get; init; }
    public required string Descricao { get; init; }
    public AvalaraUnidade? UnidadeMedida { get; init; }
}

internal sealed record AvalaraUnidade
{
    public required string Codigo { get; init; }
}

internal sealed record AvalaraImpostoItem
{
    public AvalaraIcms? Icms { get; init; }
    public AvalaraIpi? Ipi { get; init; }
    public AvalaraPis? Pis { get; init; }
    public AvalaraCofins? Cofins { get; init; }
    public AvalaraIi? Ii { get; init; }
    public AvalaraIcmsSt? Icmsst { get; init; }
    public AvalaraIssqn? Issqn { get; init; }
}

internal sealed record AvalaraIcms
{
    /// <summary>Origem da mercadoria (Tabela A do CST).</summary>
    public int? SituacaoTributariaICMSTabA { get; init; }

    /// <summary>Tributação (Tabela B do CST) — campo do schema.</summary>
    public int? SituacaoTributariaICMSTabB { get; init; }

    public required decimal ValorBaseICMS { get; init; }
    public required decimal AliquotaICMS { get; init; }
    public required decimal ValorICMS { get; init; }
    public decimal? ValorBaseIsentoICMS { get; init; }
    public decimal? ValorBaseOutrosICMS { get; init; }
}

internal sealed record AvalaraIpi
{
    public int? SituacaoTributariaIPI { get; init; }
    public required decimal BaseCalculoIPI { get; init; }
    public required decimal AliquotaIPI { get; init; }
    public required decimal ValorIPI { get; init; }
    public decimal? ValorBaseIsentoIPI { get; init; }
    public decimal? ValorBaseOutrosIPI { get; init; }
}

internal sealed record AvalaraPis
{
    public int? SituacaoTributariaPIS { get; init; }
    public required decimal BaseCalculoPIS { get; init; }
    public required decimal AliquotaPIS { get; init; }
    public required decimal ValorPIS { get; init; }
}

internal sealed record AvalaraCofins
{
    public int? SituacaoTributariaCOFINS { get; init; }
    public required decimal BaseCalculoCOFINS { get; init; }
    public required decimal AliquotaCOFINS { get; init; }
    public required decimal ValorCOFINS { get; init; }
}

internal sealed record AvalaraIi
{
    public required decimal BaseCalculoII { get; init; }
    public required decimal AliquotaII { get; init; }
    public required decimal ValorII { get; init; }
}

internal sealed record AvalaraIcmsSt
{
    public required decimal ValorBaseICMSST { get; init; }
    public required decimal AliquotaICMSST { get; init; }
    public required decimal ValorICMSST { get; init; }
}

// ISS e ISS retido dividem o bloco: cada metade é opcional.
internal sealed record AvalaraIssqn
{
    public decimal? BaseCalculoISSQN { get; init; }
    public decimal? AliquotaISSQN { get; init; }
    public decimal? ValorISSQN { get; init; }
    public decimal? ValorBaseISSRetido { get; init; }
    public decimal? AliquotaISSRetido { get; init; }
    public decimal? ValorISSRetido { get; init; }
}

// Entrada do array genérico, só para o grupo da Reforma. CST e classificação vêm aninhados com o código do imposto.
internal sealed record AvalaraImposto
{
    public required AvalaraCodigo Imposto { get; init; }
    public AvalaraCodigoDeImposto? SituacaoTributariaImposto { get; init; }
    public AvalaraCodigoDeImposto? ClassificacaoTributariaImposto { get; init; }
    public required decimal ValorBaseTributo { get; init; }
    public required decimal AliquotaTributo { get; init; }
    public required decimal ValorTributoBruto { get; init; }
}

internal sealed record AvalaraCodigo
{
    public required string Codigo { get; init; }
}

internal sealed record AvalaraCodigoDeImposto
{
    public required AvalaraCodigo Imposto { get; init; }
    public required string Codigo { get; init; }
}

internal sealed record AvalaraTotais
{
    public decimal? ValorMercadorias { get; init; }
    public required decimal ValorDocumento { get; init; }
}
