namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>Opções globais do feed do D365 (as por tenant ficam nas settings do perfil). Pública para o bind no composition root.</summary>
public sealed class D365ChangeFeedOptions
{
    /// <summary>
    /// Maior espera aceita na hora diante de 429/503 com <c>Retry-After</c>. Acima disso o feed desiste e o
    /// worker adia o tenant. Fica abaixo do prazo do lease, para a espera não deixar o lease vencer.
    /// </summary>
    public TimeSpan MaxThrottleWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tentativas por requisição (a primeira mais as repetições) antes de desistir por throttling.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Quanto o horizonte estável de cada página recua do header <c>Date</c> da primeira resposta (design D16).
    /// Cobre a resolução de segundo do <c>SysModifiedDateTime</c> e a diferença entre o relógio do web server e
    /// o que carimba o registro. Mínimo 1s: abaixo disso, duas gravações no mesmo segundo poderiam ter a segunda
    /// suprimida.
    /// </summary>
    public TimeSpan StampSettleMargin { get; set; } = TimeSpan.FromSeconds(10);
}
