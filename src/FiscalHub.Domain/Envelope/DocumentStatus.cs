namespace FiscalHub.Domain.Envelope;

/// <summary>Estado fiscal do documento, usado para roteamento na esteira.</summary>
public enum DocumentStatus
{
    /// <summary>Emitido e autorizado.</summary>
    Issued,

    /// <summary>Cancelado na origem.</summary>
    Cancelled,
}
