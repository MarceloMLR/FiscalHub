namespace FiscalHub.Application.Outbound;

/// <summary>
/// Recibo do envio (fase 1). Em plataformas assíncronas (ex.: Avalara) traz o id externo (o GUID)
/// e status <see cref="IntegrationStatus.Submitted"/>; em síncronas pode já vir
/// <see cref="IntegrationStatus.Confirmed"/>.
/// </summary>
public sealed record IntegrationReceipt
{
    /// <summary>Identificador devolvido pela plataforma (ex.: GUID da Avalara) para consulta posterior.</summary>
    public required string ExternalId { get; init; }

    public required IntegrationStatus Status { get; init; }
}

/// <summary>Resultado de uma consulta de status (fase 2).</summary>
public sealed record IntegrationResult
{
    public required IntegrationStatus Status { get; init; }

    /// <summary>Detalhe legível quando há erro de integração (o motivo da rejeição).</summary>
    public string? Message { get; init; }
}
