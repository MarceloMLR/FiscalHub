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
}
