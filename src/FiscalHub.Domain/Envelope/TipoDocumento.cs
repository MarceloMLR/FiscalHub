namespace FiscalHub.Domain.Envelope;

/// <summary>
/// Tipo de documento fiscal que a esteira transporta. Cada tipo tem seu próprio
/// modelo de domínio coeso (a "verdade" do conector). O envelope carrega apenas o
/// tipo para a esteira rotear de forma uniforme, sem conhecer o conteúdo.
/// </summary>
public enum TipoDocumento
{
    /// <summary>Nota Fiscal eletrônica de mercadoria (modelo 55). Marco 1.</summary>
    Mercadoria55,

    /// <summary>Conhecimento de Transporte eletrônico (modelo 57). Marco 2.</summary>
    Transporte57,

    /// <summary>Nota Fiscal de Serviço eletrônica (NFS-e). Marco 2.</summary>
    ServicoNfse,
}
