namespace FiscalHub.Domain.Mercadoria;

/// <summary>
/// Parte envolvida na operação (emitente ou destinatário), como consta no documento.
/// Modela apenas a "verdade" da nota.
///
/// Dados que a nota NÃO traz — por exemplo o código interno do parceiro no ERP do
/// cliente — não entram aqui. Isso é responsabilidade da etapa de ENRIQUECIMENTO da
/// esteira (um marco futuro), para não poluir o modelo de domínio com campos que não
/// pertencem à verdade do documento.
/// </summary>
public sealed record Participante
{
    /// <summary>CNPJ (ou CPF) da parte.</summary>
    public required string Documento { get; init; }

    /// <summary>Razão social / nome.</summary>
    public required string Nome { get; init; }

    /// <summary>Inscrição estadual, quando houver.</summary>
    public string? InscricaoEstadual { get; init; }

    /// <summary>Código IBGE do município da parte, quando informado.</summary>
    public string? CodigoMunicipio { get; init; }
}
