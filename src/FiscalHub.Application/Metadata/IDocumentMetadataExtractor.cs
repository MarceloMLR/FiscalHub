namespace FiscalHub.Application.Metadata;

/// <summary>
/// Extrai os metadados de agrupamento de um documento (empresa, filial, data). Por tipo, porque a
/// origem varia — na NF-e sai do CNPJ do emitente; noutro documento pode ser outro campo.
/// </summary>
public interface IDocumentMetadataExtractor<TDocument>
{
    DocumentMetadata Extract(TDocument document);
}

/// <summary>Metadados usados para agrupar documentos (empresa/filial/dia) no dashboard.</summary>
public sealed record DocumentMetadata
{
    /// <summary>Código da empresa (ex.: raiz do CNPJ, 8 dígitos, ou um código interno).</summary>
    public required string CompanyCode { get; init; }

    /// <summary>Código da filial (ex.: ordem do CNPJ, "0001" = matriz, ou um código interno).</summary>
    public required string BranchCode { get; init; }

    /// <summary>Data de referência (dia) usada no agrupamento.</summary>
    public required DateOnly ReferenceDate { get; init; }

    /// <summary>Número do documento (ex.: nNF da NF-e).</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Modelo do documento (ex.: "55").</summary>
    public required string DocumentModel { get; init; }
}
