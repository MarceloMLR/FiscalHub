namespace FiscalHub.Domain.Envelope;

/// <summary>
/// Estado do documento sob a ótica da INTEGRAÇÃO — não é status tributário. A esteira usa isso
/// para rotear: uma emissão segue para o destino; um cancelamento segue por outro caminho.
/// (Correção/CC-e fica como extensão de um marco futuro, para manter o envelope mínimo.)
/// </summary>
public enum DocumentStatus
{
    /// <summary>Documento emitido e autorizado, a ser despachado ao compliance.</summary>
    Issued,

    /// <summary>Documento cancelado na origem; roteado para o fluxo de cancelamento.</summary>
    Cancelled,
}
