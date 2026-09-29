namespace FiscalHub.Domain.Goods;

/// <summary>
/// NF-e de mercadoria (modelo 55) no modelo de domínio do hub. Representa uma nota já autorizada
/// pela SEFAZ; o hub não a emite nem revalida.
/// </summary>
public sealed record GoodsInvoice
{
    /// <summary>Chave de acesso da NF-e (44 dígitos).</summary>
    public required string AccessKey { get; init; }

    /// <summary>Modelo do documento ("55").</summary>
    public required string Model { get; init; }

    /// <summary>Série da nota.</summary>
    public required string Series { get; init; }

    /// <summary>Número da nota.</summary>
    public required string Number { get; init; }

    /// <summary>Data e hora de emissão.</summary>
    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>
    /// O dia fiscal da nota como a origem o registra, sem hora e sem fuso, quando ela o guarda separado do instante da
    /// emissão (no D365, o <c>FiscalDocumentDate</c>). <c>null</c> = a origem não o separa (ex.: XML), e o dia é a data da
    /// <see cref="IssueDate"/> no fuso que ela traz.
    /// </summary>
    public DateOnly? FiscalDate { get; init; }

    /// <summary>Data de entrada ou saída, quando a origem a traz.</summary>
    public DateTimeOffset? EntryExitDate { get; init; }

    /// <summary>
    /// Emissão própria ou de terceiros, quando a origem diz. <c>null</c> = a origem não diz (ex.: XML), e quem precisar
    /// saber qual parte é o estabelecimento próprio decide por outro meio.
    /// </summary>
    public Issuance? Issuance { get; init; }

    /// <summary>
    /// O estabelecimento próprio que escritura a nota, quando a origem o informa (D365). <c>null</c> = a origem não diz
    /// (ex.: XML).
    /// </summary>
    public Establishment? Establishment { get; init; }

    /// <summary>Emitente da nota.</summary>
    public required Party Issuer { get; init; }

    /// <summary>Destinatário da nota.</summary>
    public required Party Recipient { get; init; }

    /// <summary>Município do fato gerador do IBS/CBS (campo cMunFGIBS).</summary>
    public string? IbsCbsTaxableMunicipality { get; init; }

    /// <summary>Itens da nota.</summary>
    public required IReadOnlyList<GoodsInvoiceItem> Items { get; init; }

    /// <summary>Valor total da nota.</summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>Valor total das mercadorias (vProd), quando a origem o traz.</summary>
    public decimal? GoodsAmount { get; init; }
}
