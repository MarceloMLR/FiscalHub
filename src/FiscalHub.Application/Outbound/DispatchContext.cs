using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Outbound;

/// <summary>
/// Contexto que acompanha um despacho: a quem pertence, como correlacionar nos logs, e qual a
/// operação fiscal (carga x cancelamento). É aqui que o status fiscal cumpre seu papel funcional
/// — definir O QUE enviar à plataforma —, sem ser protagonista do dashboard.
/// </summary>
public sealed record DispatchContext
{
    public required string TenantId { get; init; }

    public required string CorrelationId { get; init; }

    /// <summary>
    /// Operação derivada do status fiscal: <see cref="DocumentStatus.Issued"/> → carga;
    /// <see cref="DocumentStatus.Cancelled"/> → cancelamento.
    /// </summary>
    public required DocumentStatus Operation { get; init; }
}
