using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Endereço de um documento na origem. É o que viaja "magro" pela esteira (claim-check): o evento
/// carrega a referência; o consumidor usa ela para buscar o documento completo.
/// </summary>
public sealed record DocumentReference
{
    public required string TenantId { get; init; }

    public required DocumentType Type { get; init; }

    /// <summary>Chave de negócio (ex.: chave de acesso da NF-e). Base da idempotência/dedup.</summary>
    public required string NaturalKey { get; init; }

    /// <summary>
    /// Localizador específico da origem — o que aquele adapter precisa para achar o documento
    /// (caminho no Blob, id no ERP, etc.). Só o adapter da origem sabe interpretá-lo.
    /// </summary>
    public required string Locator { get; init; }
}
