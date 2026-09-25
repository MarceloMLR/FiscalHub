namespace FiscalHub.Application.Inbound;

/// <summary>Estado persistido do feed de mudanças de um par (tenant, origem): a marca d'água e o desfecho do último poll.</summary>
public sealed record ChangeFeedCursor
{
    public required string TenantId { get; init; }

    public required string Origin { get; init; }

    /// <summary>Instante até o qual tudo o que mudou na origem já foi enfileirado. <c>null</c> = ainda não iniciado.</summary>
    public DateTimeOffset? Watermark { get; init; }

    /// <summary>Fim do último poll (com ou sem sucesso) — base do intervalo por tenant.</summary>
    public DateTimeOffset? LastPolledAt { get; init; }

    /// <summary>Não consultar antes deste instante (throttling pedido pela origem).</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>Falhas seguidas; volta a zero no primeiro sucesso.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>Mensagem do último erro, para diagnóstico. Limpa no sucesso.</summary>
    public string? LastError { get; init; }
}
