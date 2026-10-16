using System.Text.Json.Serialization;

namespace FiscalHub.Application.Connectors;

/// <summary>
/// A gravação do perfil de conector (<c>PUT /connector</c>). O tenant vem do usuário logado, e não do corpo (ADR-0028).
/// As settings podem trazer o valor de um segredo como campo de escrita (<see cref="ConnectorSecretFields"/>), que vai
/// para o cofre (ADR-0027). Chamados ausentes (<c>null</c>) mantêm o adapter e as settings já gravados, e contas ausentes
/// mantêm as gravadas (ADR-0036). O ambiente não é campo da gravação: é da instância, e um corpo antigo que ainda o traga
/// tem o campo ignorado (change <c>platform-account-and-connectors</c>, D6). A lista de módulos do tenant também não é
/// mais: os módulos são ligados em cada conector (D7). O campo <see cref="Modules"/> existe só para ser recusado — um
/// dashboard que não recarregou e ainda o manda recebe 400, e não um sucesso em que nada aconteceu.
/// <para>O <see cref="OutboundDispatch"/> é a chave de envio ao conector de saída (ADR-0040). Ausente (<c>null</c>),
/// mantém a gravada — um dashboard que não recarregou não liga o envio de um tenant que o desligou.</para>
/// </summary>
public sealed record ConnectorProfileRequest(
    string InboundAdapter,
    string? InboundSettings,
    string? SupportAdapter = null,
    string? SupportSettings = null,
    IReadOnlyList<PlatformAccountRequest>? Accounts = null,
    IReadOnlyList<string>? Modules = null,
    bool? OutboundDispatch = null)
{
    // As settings podem carregar segredo: nunca entram no texto, que pode acabar num log.
    public override string ToString()
        => $"ConnectorProfileRequest {{ InboundAdapter = {InboundAdapter}, SupportAdapter = {SupportAdapter}, "
            + $"Accounts = [{string.Join(", ", (Accounts ?? []).Select(a => a.Platform))}] }}";
}

/// <summary>
/// Uma conta de plataforma na gravação (ADR-0036). O Client Secret é campo de escrita: vai para o cofre, e a conta guarda
/// só a referência; ausente ou vazio, mantém o que estava configurado. A referência nunca vem da requisição: o campo
/// <see cref="ClientSecretRef"/> existe só para ser recusado (<c>connector-secret-references</c>). Conectores ausentes
/// (<c>null</c>) mantêm os gravados da conta.
/// </summary>
public sealed record PlatformAccountRequest(
    string Platform,
    string? ClientId = null,
    string? ClientSecret = null,
    string? TokenUrl = null,
    IReadOnlyList<PlatformConnectorRequest>? Connectors = null,
    string? ClientSecretRef = null)
{
    // O segredo e a referência nunca entram no texto, que pode acabar num log.
    public override string ToString() => $"PlatformAccountRequest {{ Platform = {Platform}, ClientId = {ClientId} }}";
}

/// <summary>
/// Um conector na gravação: o adapter, que é a chave, as settings do produto (JSON), e o que ele liga — os módulos e as
/// integrações, cada integração no módulo que o adapter declara para ela. Os dois ausentes (<c>null</c>) mantêm o que o
/// conector tem gravado.
/// </summary>
public sealed record PlatformConnectorRequest(
    string Adapter,
    string? Settings = null,
    IReadOnlyList<string>? Modules = null,
    IReadOnlyList<ConnectorIntegrationRequest>? Integrations = null)
{
    public override string ToString() => $"PlatformConnectorRequest {{ Adapter = {Adapter} }}";
}

/// <summary>
/// Uma integração ligada na gravação: a chave, o caminho (vazio deixa a integração inativa; ausente mantém o gravado) e o
/// tamanho do lote.
/// </summary>
public sealed record ConnectorIntegrationRequest(string Key, string? Endpoint = null, int? BatchSize = null);

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
/// campo de escrita, e o mapa <see cref="Secrets"/> — caminho (<c>platform.Avalara.clientSecret</c>) → configurado e quando.
/// Nunca o valor, nem parte dele, nem a referência (ADR-0027). Os <see cref="Modules"/> vêm com o padrão aplicado.
/// <para>O <see cref="OutboundDispatch"/> é a chave de envio: é por esta leitura que o formulário de Configurações a
/// desenha com o estado gravado (ADR-0040). A tela do documento a lê pelo <c>/info</c>, que qualquer papel alcança.</para>
/// </summary>
public sealed record ConnectorProfileView(
    string TenantId,
    string InboundAdapter,
    string InboundSettings,
    string? SupportAdapter,
    string SupportSettings,
    IReadOnlyDictionary<string, SecretStatus> Secrets,
    IReadOnlyList<string> Modules,
    IReadOnlyList<PlatformAccountView> Accounts,
    bool OutboundDispatch);

/// <summary>
/// A conta na leitura: a plataforma, o Client ID e o endpoint de token. O estado do segredo vai no mapa de segredos da
/// leitura, em <c>platform.&lt;plataforma&gt;.clientSecret</c>; nunca o valor nem a referência.
/// </summary>
public sealed record PlatformAccountView(string Platform, string? ClientId, string? TokenUrl, IReadOnlyList<PlatformConnectorView> Connectors);

/// <summary>
/// Um conector na leitura: as settings sem as referências e sem campo de escrita, os módulos ligados, na ordem da barra
/// lateral, e as integrações ligadas.
/// </summary>
public sealed record PlatformConnectorView(
    string Adapter, string Settings, IReadOnlyList<string> Modules, IReadOnlyList<ConnectorIntegrationView> Integrations);

/// <summary>
/// Uma integração ligada na leitura: a chave, o módulo dela, o caminho e, só onde a API aceita lote, o tamanho dele (6.6).
/// <see cref="WithoutPath"/> diz que ela está sem caminho — inativa, e pendente na tela (D5).
/// </summary>
public sealed record ConnectorIntegrationView(
    string Key,
    string Module,
    string? Endpoint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BatchSize,
    bool WithoutPath);

/// <summary>Se o segredo está no cofre e a data da versão atual. Sem valor, sem referência.</summary>
public sealed record SecretStatus(bool Configured, DateTimeOffset? UpdatedOn);
