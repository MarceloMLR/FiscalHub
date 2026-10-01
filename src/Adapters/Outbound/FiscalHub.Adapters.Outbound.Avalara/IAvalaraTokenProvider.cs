using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// O token de acesso à Avalara (OAuth client credentials), por credencial do tenant no ambiente ativo (ADR-0027).
/// Mantido <c>internal</c>: o detalhe de autenticação é da Avalara e não vaza do adapter (camada anticorrupção).
/// </summary>
internal interface IAvalaraTokenProvider
{
    /// <summary>
    /// O token da credencial da seção. Na composição padrão, ou devolve um token, ou lança — a falta de credencial,
    /// de segredo ou a recusa da plataforma é <see cref="Application.Outbound.DispatchRejectedException"/>. Só a
    /// composição sem autenticação, pedida explicitamente, devolve <see cref="AvalaraAccessToken.None"/>.
    /// </summary>
    Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default);

    /// <summary>Tira o token do cache, se ele ainda for o que está lá. O dispatcher chama no 401 com token do cache.</summary>
    void Invalidate(AvalaraAccessToken token);

    /// <summary>Esquece a recusa lembrada e os tokens do tenant, em todos os ambientes. Chamado ao salvar o perfil.</summary>
    void Forget(string tenantId);

    /// <summary>
    /// O teste de credencial (change module-navigation-and-integration-panel, D10): troca um token NOVO, sem o cache e sem
    /// a recusa lembrada, porque o botão responde "essa credencial funciona agora". Nunca lança pela plataforma: o
    /// desfecho vem no veredito. O sucesso esquece a recusa e os tokens do tenant, como o salvar do perfil, e guarda o token
    /// novo. A recusa fica lembrada para o envio.
    /// </summary>
    Task<CredentialTestOutcome> ProbeAsync(AvalaraOutboundSettings settings, CancellationToken ct = default);
}

/// <summary>
/// Um token de acesso: o valor, se acabou de vir do endpoint (<see cref="IsFresh"/>) e a entrada do cache de onde veio.
/// Não é <c>record</c> de propósito: o <see cref="ToString"/> traz só o tenant e o ambiente, e um log descuidado do
/// objeto não vaza o token.
/// </summary>
internal sealed class AvalaraAccessToken
{
    /// <summary>"Sem token", explícito: só a composição sem autenticação o devolve.</summary>
    public static readonly AvalaraAccessToken None = new(string.Empty, string.Empty, string.Empty, isFresh: false, key: null);

    internal AvalaraAccessToken(string tenantId, string environment, string value, bool isFresh, object? key)
    {
        TenantId = tenantId;
        Environment = environment;
        Value = value;
        IsFresh = isFresh;
        Key = key;
    }

    public string TenantId { get; }

    public string Environment { get; }

    public string Value { get; }

    /// <summary>Acabou de ser emitido: um 401 com ele não se conserta pedindo outro (ADR-0027).</summary>
    public bool IsFresh { get; }

    public bool IsNone => ReferenceEquals(this, None);

    /// <summary>A entrada do cache de onde o token veio (opaca fora do provider).</summary>
    internal object? Key { get; }

    public override string ToString() => IsNone ? "AvalaraAccessToken { None }" : $"AvalaraAccessToken {{ Tenant = {TenantId}, Environment = {Environment} }}";
}

/// <summary>Stub sem autenticação: só por pedido explícito (<c>UseAvalaraWithoutAuthentication</c>). Não lê credencial.</summary>
internal sealed class NoOpAvalaraTokenProvider : IAvalaraTokenProvider
{
    public Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
        => Task.FromResult(AvalaraAccessToken.None);

    public void Invalidate(AvalaraAccessToken token)
    {
    }

    public void Forget(string tenantId)
    {
    }

    public Task<CredentialTestOutcome> ProbeAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
        => Task.FromResult(new CredentialTestOutcome(CredentialTestVerdict.Incomplete,
            "O adapter está composto sem autenticação (UseAvalaraWithoutAuthentication): não há credencial para testar."));
}
