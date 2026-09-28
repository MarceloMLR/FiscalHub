namespace FiscalHub.Domain.Goods;

/// <summary>
/// O estabelecimento próprio que escritura a nota, como a origem o informa: o emitente na emissão própria, o destinatário
/// na de terceiros. É por ele que a nota é agrupada (empresa e filial). Os dois campos andam juntos: a origem que conhece
/// um conhece o outro.
/// </summary>
public sealed record Establishment
{
    /// <summary>CNPJ completo do estabelecimento, só com dígitos.</summary>
    public required string TaxId { get; init; }

    /// <summary>Código do estabelecimento na origem, como veio (ex.: "Matriz" no D365). Pode vir vazio.</summary>
    public required string Code { get; init; }
}
