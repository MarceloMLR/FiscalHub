namespace FiscalHub.Application.Inbound;

/// <summary>
/// Lançada pelo feed quando a origem pede para esperar mais do que ele aceita esperar na hora (429 com
/// <c>Retry-After</c> acima do teto, ou retentativas esgotadas). O worker adia o tenant pelo tempo pedido,
/// sem avançar a marca — continuar batendo durante o throttling só alonga a espera.
/// </summary>
public sealed class ChangeFeedThrottledException : Exception
{
    public ChangeFeedThrottledException(TimeSpan retryAfter, string message) : base(message)
        => RetryAfter = retryAfter;

    /// <summary>Quanto a origem pediu para esperar antes da próxima consulta.</summary>
    public TimeSpan RetryAfter { get; }
}
