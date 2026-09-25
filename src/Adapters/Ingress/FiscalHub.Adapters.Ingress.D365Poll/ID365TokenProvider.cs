namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Gancho para o bearer token do Entra ID usado no OData do F&amp;O. Mantido <c>internal</c>, no espelho do
/// <c>IAvalaraTokenProvider</c>: o detalhe de autenticação é do D365 e não vaza do adapter. Duas
/// implementações: client credentials (produção) e sessão do Azure CLI (só desenvolvimento).
/// </summary>
internal interface ID365TokenProvider
{
    /// <summary>Devolve um token válido para o recurso do ambiente F&amp;O da conexão.</summary>
    Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default);
}

/// <summary>Para onde e como autenticar: tenant do FiscalHub, URL do ambiente F&amp;O e as credenciais do perfil.</summary>
internal sealed record D365Connection(string TenantId, Uri EnvironmentUrl, D365AuthSettings? Auth)
{
    /// <summary>Escopo do token: o próprio ambiente F&amp;O (<c>https://&lt;ambiente&gt;/.default</c>).</summary>
    public string Scope => $"{EnvironmentUrl.GetLeftPart(UriPartial.Authority)}/.default";
}
