namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Gancho para o token de acesso à Avalara (OAuth client credentials). Mantido <c>internal</c>:
/// o detalhe de autenticação é da Avalara e não vaza do adapter (camada anticorrupção).
/// O cache real (token por empresa/tenant, validade 24h, thread-safe) substitui o
/// <see cref="NoOpAvalaraTokenProvider"/> numa fatia futura.
/// </summary>
internal interface IAvalaraTokenProvider
{
    /// <summary>Devolve o token para o tenant; cadeia vazia significa "sem autenticação" (stub).</summary>
    Task<string> GetTokenAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>Stub no-op: não autentica. Placeholder até o cache de token real ser implementado.</summary>
internal sealed class NoOpAvalaraTokenProvider : IAvalaraTokenProvider
{
    public Task<string> GetTokenAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
}
