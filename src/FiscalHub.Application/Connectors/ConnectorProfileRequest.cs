namespace FiscalHub.Application.Connectors;

/// <summary>
/// A gravação do perfil de conector (<c>PUT /connector</c>). O tenant vem do usuário logado, e não do corpo (ADR-0028).
/// As settings podem trazer o valor de um segredo como campo de escrita (<see cref="ConnectorSecretFields"/>), que vai
/// para o cofre (ADR-0027). Chamados ausentes (<c>null</c>) mantêm o adapter e as settings já gravados.
/// </summary>
public sealed record ConnectorProfileRequest(
    string Environment,
    string InboundAdapter,
    string? InboundSettings,
    string OutboundAdapter,
    string? OutboundSettings,
    string? SupportAdapter = null,
    string? SupportSettings = null)
{
    // As settings podem carregar segredo: nunca entram no texto, que pode acabar num log.
    public override string ToString()
        => $"ConnectorProfileRequest {{ Environment = {Environment}, InboundAdapter = {InboundAdapter}, "
            + $"OutboundAdapter = {OutboundAdapter}, SupportAdapter = {SupportAdapter} }}";
}

public enum ConnectorProfileSaveStatus
{
    Saved,

    /// <summary>As settings falharam na validação. Nada foi gravado, nem no cofre nem no perfil.</summary>
    Invalid,

    /// <summary>O cofre não aceitou um segredo. O perfil não foi gravado.</summary>
    SecretStoreFailed,
}

/// <summary>O desfecho da gravação. Os problemas citam o campo, e nunca o valor.</summary>
public sealed record ConnectorProfileSaveResult(ConnectorProfileSaveStatus Status, IReadOnlyList<string> Problems)
{
    public string Message => string.Join(" ", Problems);
}

/// <summary>
/// A leitura do perfil de conector (<c>GET /connector</c>): as settings sem os campos de referência e sem nenhum
/// campo de escrita, e o mapa <see cref="Secrets"/> — caminho (<c>outbound.sandbox.clientSecret</c>) → configurado e
/// quando. Nunca o valor, nem parte dele, nem a referência (ADR-0027).
/// </summary>
public sealed record ConnectorProfileView(
    string TenantId,
    string Environment,
    string InboundAdapter,
    string InboundSettings,
    string OutboundAdapter,
    string OutboundSettings,
    string? SupportAdapter,
    string SupportSettings,
    IReadOnlyDictionary<string, SecretStatus> Secrets);

/// <summary>Se o segredo está no cofre e a data da versão atual. Sem valor, sem referência.</summary>
public sealed record SecretStatus(bool Configured, DateTimeOffset? UpdatedOn);
