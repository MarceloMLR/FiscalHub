namespace FiscalHub.Application.Connectors;

/// <summary>
/// A escolha, por tenant, da implementação de uma porta que depende do ERP: o diretório de empresas e a descoberta por
/// período (change erp-company-directory-and-card-filters, D2 e D3). Vale a do adapter de entrada do perfil, pela
/// comparação exata da origem, como na resolução do source. Sem perfil, ou sem implementação para o adapter, vale o
/// fallback de desenvolvimento, quando o host o registrou: só em Development, por um registro explícito do host. O
/// fallback nunca responde no lugar de uma implementação que existe.
/// </summary>
public static class InboundAdapterChoice
{
    /// <summary>
    /// A chave de serviço com que um adapter registra o fallback de desenvolvimento, no lugar da implementação comum. Assim
    /// ele não entra na lista das implementações, e fora de Development simplesmente não existe.
    /// </summary>
    public const string DevelopmentFallbackKey = "development-fallback";

    /// <summary>
    /// As candidatas, em ordem: a do adapter de entrada, e depois o fallback, quando existe e é outro. Quem precisa de uma
    /// só usa a primeira (<see cref="Pick"/>); o reprocesso pergunta a cada uma, nessa ordem (D5).
    /// </summary>
    public static IReadOnlyList<T> Candidates<T>(IEnumerable<T> implementations, Func<T, string> origin, string? inboundAdapter, T? developmentFallback)
        where T : class
    {
        T? own = inboundAdapter is null
            ? null
            : implementations.FirstOrDefault(i => string.Equals(origin(i), inboundAdapter, StringComparison.Ordinal));

        if (own is null)
        {
            return developmentFallback is null ? [] : [developmentFallback];
        }

        return developmentFallback is null || ReferenceEquals(own, developmentFallback) ? [own] : [own, developmentFallback];
    }

    /// <summary>A implementação que responde pelo tenant, ou nenhuma.</summary>
    public static T? Pick<T>(IEnumerable<T> implementations, Func<T, string> origin, string? inboundAdapter, T? developmentFallback)
        where T : class
        => Candidates(implementations, origin, inboundAdapter, developmentFallback).FirstOrDefault();
}
