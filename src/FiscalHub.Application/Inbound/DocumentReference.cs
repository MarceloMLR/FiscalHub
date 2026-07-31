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

    /// <summary>
    /// Gatilho que originou o disparo. Define a política de idempotência: <c>Event</c> (padrão)
    /// dedupa; <c>Manual</c> reprocessa mesmo em estado terminal. Mensagens antigas, sem o campo,
    /// caem no padrão <c>Event</c>.
    /// </summary>
    public IngestionTrigger Trigger { get; init; } = IngestionTrigger.Event;

    /// <summary>
    /// Modo da integração que originou o disparo, para exibição (<c>Manual</c>, <c>ScheduledDaily</c>,
    /// <c>ScheduledOnce</c>). <c>null</c> = chegou por evento/tempo real (o caminho por evento não passa
    /// pelo runner). É só rótulo — a política de idempotência continua no <see cref="Trigger"/>.
    /// </summary>
    public string? SourceMode { get; init; }
}
