namespace FiscalHub.Adapters.Outbound.Avalara;

// DTOs que espelham o subconjunto do contrato (god json) da Avalara usado por uma NF-e 55.
// São internal: o formato externo fica preso dentro do adapter (camada anticorrupção).

internal sealed record AvalaraDocument
{
    public string? ChaveNFe { get; init; }
    public int Modelo { get; init; }
    public string? NumeroDocumento { get; init; }
    public AvalaraParceiro? Parceiro { get; init; }
    public List<AvalaraItem> Itens { get; init; } = [];
}

internal sealed record AvalaraParceiro
{
    /// <summary>Código interno do parceiro — preenchido pelo enriquecimento (fatia futura).</summary>
    public string? Codigo { get; init; }
    public string? Nome { get; init; }
    public string? Cnpj { get; init; }
}

internal sealed record AvalaraItem
{
    public int NumeroSequencia { get; init; }
    public int Cfop { get; init; }
    public decimal ValorTotal { get; init; }
    public List<AvalaraImposto> Impostos { get; init; } = [];
}

// No contrato da Avalara, CST e cClassTrib vêm aninhados num objeto { codigo: "..." }.
internal sealed record AvalaraImposto
{
    public AvalaraCodigo? Imposto { get; init; }                        // { codigo: "IBS" }
    public AvalaraCodigo? SituacaoTributariaImposto { get; init; }      // CST
    public AvalaraCodigo? ClassificacaoTributariaImposto { get; init; } // cClassTrib
    public decimal ValorBaseTributo { get; init; }
    public decimal ValorTributoBruto { get; init; }
}

internal sealed record AvalaraCodigo
{
    public string? Codigo { get; init; }
}
