namespace FiscalHub.Application.Inbound;

/// <summary>Resumo de uma passada do worker de feed de mudanças, para o log da casca de infraestrutura.</summary>
public sealed record ChangeFeedPassSummary
{
    /// <summary>Tenants cuja origem foi consultada (com ou sem sucesso).</summary>
    public int TenantsPolled { get; init; }

    /// <summary>Referências publicadas na fila de descoberta.</summary>
    public int ReferencesEnqueued { get; init; }

    /// <summary>Tenants pulados porque outra réplica detinha o lease.</summary>
    public int LeasesBusy { get; init; }

    /// <summary>Tenants cujo lease foi perdido no meio do poll (a marca não foi gravada; não conta como falha).</summary>
    public IReadOnlyList<string> LeasesLost { get; init; } = [];

    /// <summary>Tenants que falharam nesta passada, com a mensagem do erro.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Tenants que pararam no teto de páginas sem a marca passar da de início: só releram a janela de
    /// sobreposição. Se repetir, há mais linhas na janela do que <c>teto × página</c> — ajuste a página.
    /// </summary>
    public IReadOnlyList<string> Stalled { get; init; } = [];
}
