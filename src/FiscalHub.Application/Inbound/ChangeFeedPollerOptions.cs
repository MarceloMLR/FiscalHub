namespace FiscalHub.Application.Inbound;

/// <summary>Opções globais do worker de feed de mudanças (as por tenant ficam na seção <c>poll</c> do perfil).</summary>
public sealed class ChangeFeedPollerOptions
{
    /// <summary>Prazo do lease por tenant; renovado a cada página. Maior que a espera máxima por throttling.</summary>
    public TimeSpan LeaseTtl { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Quantas páginas um tenant consome numa passada, para um delta grande não monopolizar o ciclo.</summary>
    public int MaxPagesPerPass { get; set; } = 20;

    /// <summary>Identidade desta réplica no lease. Uma por processo.</summary>
    public string OwnerId { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
}
