namespace FiscalHub.Domain.Goods;

/// <summary>
/// Emissão própria ou de terceiros, do ponto de vista do estabelecimento que escritura a nota (o IND_EMIT do SPED).
/// Diz qual parte da nota é o estabelecimento próprio: o emitente na emissão própria, o destinatário na de terceiros.
/// </summary>
public enum Issuance
{
    /// <summary>Emissão própria: o estabelecimento é o emitente.</summary>
    Own,

    /// <summary>Emissão de terceiros: o estabelecimento é o destinatário.</summary>
    ThirdParty,
}
