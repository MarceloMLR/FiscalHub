namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Linha de persistência de um lease (um por recurso). Dono nulo = livre.</summary>
internal sealed class LeaseRow
{
    public required string Resource { get; set; }

    public string? Owner { get; set; }

    /// <summary>Prazo em ticks UTC; vencido (ou zero, quando liberado) = qualquer um pode tomar.</summary>
    public long ExpiresTicks { get; set; }
}
