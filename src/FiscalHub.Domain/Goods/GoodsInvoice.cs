namespace FiscalHub.Domain.Goods;

/// <summary>
/// Modelo de domínio da NF-e de mercadoria (modelo 55) — a "verdade" do conector para este tipo
/// de documento.
///
/// Representa uma nota JÁ emitida e autorizada pela SEFAZ (cStat 100). O hub não emite, não assina
/// com certificado e não revalida a estrutura (XSD). Este modelo permanece estável quando se troca
/// o ERP de origem ou o sistema de compliance de destino — por isso é o centro do hexágono.
/// </summary>
public sealed record GoodsInvoice
{
    /// <summary>Chave de acesso da NF-e (44 dígitos) — chave natural do documento.</summary>
    public required string AccessKey { get; init; }

    /// <summary>Modelo do documento ("55").</summary>
    public required string Model { get; init; }

    /// <summary>Série da nota.</summary>
    public required string Series { get; init; }

    /// <summary>Número da nota.</summary>
    public required string Number { get; init; }

    /// <summary>Data/hora de emissão.</summary>
    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>Emitente da nota.</summary>
    public required Party Issuer { get; init; }

    /// <summary>Destinatário da nota.</summary>
    public required Party Recipient { get; init; }

    /// <summary>
    /// Município de ocorrência do fato gerador do IBS/CBS (campo B12a_cMunFGIBS da NT 2025.002).
    /// Relevante para a Reforma; transportado sem cálculo.
    /// </summary>
    public string? IbsCbsTaxableMunicipality { get; init; }

    /// <summary>Itens da nota. Cada item carrega seu Grupo UB (IBS/CBS/IS).</summary>
    public required IReadOnlyList<GoodsInvoiceItem> Items { get; init; }

    /// <summary>Valor total da nota, como consta no documento.</summary>
    public required decimal TotalAmount { get; init; }
}
