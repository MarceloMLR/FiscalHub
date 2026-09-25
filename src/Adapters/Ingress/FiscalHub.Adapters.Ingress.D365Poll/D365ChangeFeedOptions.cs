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
}
