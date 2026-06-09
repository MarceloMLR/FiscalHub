using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Inbound;

/// <summary>Referência leve a um documento na origem, usada pela esteira para buscá-lo (claim-check).</summary>
public sealed record DocumentReference
{
    /// <summary>Tenant dono do documento.</summary>
    public required string TenantId { get; init; }

    /// <summary>Tipo do documento.</summary>
    public required DocumentType Type { get; init; }

    /// <summary>Chave de negócio da origem (ex.: chave de acesso da NF-e).</summary>
    public required string NaturalKey { get; init; }

    /// <summary>Localizador interpretado pelo adapter da origem (caminho no Blob, id no ERP, etc.).</summary>
    public required string Locator { get; init; }
}
