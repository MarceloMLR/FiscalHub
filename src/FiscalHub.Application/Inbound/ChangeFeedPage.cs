namespace FiscalHub.Application.Inbound;

/// <summary>Uma página do feed de mudanças: as referências descobertas e até onde a origem garante a entrega.</summary>
public sealed record ChangeFeedPage
{
    /// <summary>Referências dos documentos da página (claim-check, sem o conteúdo).</summary>
    public required IReadOnlyList<DocumentReference> References { get; init; }

    /// <summary>
    /// Marca alta garantida: tudo o que mudou até este instante já foi entregue por esta página ou pelas
    /// anteriores. <c>null</c> = a página não garante nada novo (ex.: leitura vazia sem relógio da origem),
    /// e a marca não avança.
    /// </summary>
    public DateTimeOffset? HighWatermark { get; init; }
}
