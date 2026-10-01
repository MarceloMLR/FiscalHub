using FiscalHub.Application.Auth;

namespace FiscalHub.Application.Connectors;

public enum CredentialTestStatus
{
    /// <summary>O teste respondeu, ou o freio respondeu por ele: <see cref="CredentialTestReply.Result"/>.</summary>
    Tested,

    /// <summary>O tenant não tem perfil, ou o adapter gravado não tem teste.</summary>
    Unsupported,

    /// <summary>O lado ou o ambiente pedido não existe.</summary>
    Invalid,
}

/// <summary>
/// A resposta do teste, e só isso: se funcionou, a mensagem curta da tela e, com o freio valendo, quando um novo teste é
/// aceito. Nunca o token, o segredo, nem um cabeçalho com valor. O detalhe do motivo não vai na resposta: vai para o log
/// (<see cref="CredentialTestLog"/>).
/// </summary>
public sealed record CredentialTestResult(bool Worked, string Reason, DateTimeOffset? RetryAt);

/// <summary>
/// O que o host registra de cada teste: o adapter, o ambiente, o veredito e o motivo detalhado (o código AADSTS, o status
/// HTTP, o campo que falta). É o diagnóstico que a mensagem curta da tela não carrega. Sem segredo nem token.
/// </summary>
public sealed record CredentialTestLog(string Adapter, string? Environment, CredentialTestVerdict Verdict, string Detail, bool FromBrake);

public sealed record CredentialTestReply(
    CredentialTestStatus Status, CredentialTestResult? Result, string? Problem = null, CredentialTestLog? Log = null);

/// <summary>
/// O teste da credencial gravada do tenant logado (change <c>module-navigation-and-integration-panel</c>, D9). A requisição
/// diz só o lado e, na saída, o ambiente: a credencial é a gravada, lida do cofre no servidor pelo adapter. Com uma edição
/// pendente, é a tela que pede para salvar antes. O freio (<see cref="CredentialTestBrake"/>) é o mesmo para qualquer
/// adapter.
/// </summary>
public sealed class ConnectorCredentialTestService
{
    // As mensagens da tela (revisão da prova manual, 2026-10-01): curtas e iguais para qualquer adapter. A indisponível
    // é separada de propósito: dizer "inválidos" a uma plataforma fora do ar levaria o Admin a trocar um segredo certo.
    public const string WorkedMessage = "Credenciais e conexão válidas";
    public const string RefusedMessage = "Credenciais ou ambiente inválidos";
    public const string UnavailableMessage = "Não foi possível conectar agora. Tente novamente em instantes";

    private static readonly string[] Environments = ["Sandbox", "Production"];

    private readonly IConnectorProfileStore _profiles;
    private readonly IEnumerable<IConnectorCredentialTest> _tests;
    private readonly CredentialTestBrake _brake;
    private readonly ITenantContext _tenant;

    public ConnectorCredentialTestService(
        IConnectorProfileStore profiles, IEnumerable<IConnectorCredentialTest> tests, CredentialTestBrake brake, ITenantContext tenant)
    {
        _profiles = profiles;
        _tests = tests;
        _brake = brake;
        _tenant = tenant;
    }

    public async Task<CredentialTestReply> TestAsync(string side, string? environment, CancellationToken ct = default)
    {
        ConnectorSettingsKind kind;
        if (string.Equals(side, "inbound", StringComparison.OrdinalIgnoreCase))
        {
            kind = ConnectorSettingsKind.Inbound;
            environment = null;   // a entrada tem uma credencial só
        }
        else if (string.Equals(side, "outbound", StringComparison.OrdinalIgnoreCase))
        {
            kind = ConnectorSettingsKind.Outbound;
            environment = Environments.FirstOrDefault(e => string.Equals(e, environment, StringComparison.OrdinalIgnoreCase));
            if (environment is null)
            {
                return new CredentialTestReply(CredentialTestStatus.Invalid, null,
                    $"A saída tem uma credencial por ambiente: informe {string.Join(" ou ", Environments)}.");
            }
        }
        else
        {
            return new CredentialTestReply(CredentialTestStatus.Invalid, null, "O lado do teste é inbound ou outbound.");
        }

        string tenantId = _tenant.TenantId;
        TenantConnectorProfile? profile = await _profiles.GetAsync(tenantId, ct);
        if (profile is null)
        {
            return new CredentialTestReply(CredentialTestStatus.Unsupported, null,
                "Este tenant não tem perfil de conector gravado: salve as Configurações antes de testar.");
        }

        string adapter = kind == ConnectorSettingsKind.Inbound ? profile.InboundAdapter : profile.OutboundAdapter;
        IConnectorCredentialTest? test = _tests.FirstOrDefault(
            t => t.Side == kind && string.Equals(t.Adapter, adapter, StringComparison.OrdinalIgnoreCase));
        if (test is null)
        {
            return new CredentialTestReply(CredentialTestStatus.Unsupported, null, $"O adapter {adapter} não tem teste de credencial.");
        }

        // Dentro do intervalo, o freio responde pelo teste: nenhuma requisição sai (D12).
        if (_brake.TryHeld(tenantId, test.Adapter, environment, out string held, out DateTimeOffset until))
        {
            return Tested(new CredentialTestOutcome(CredentialTestVerdict.Refused, held), until, test.Adapter, environment, fromBrake: true);
        }

        CredentialTestOutcome outcome = await test.TestAsync(profile, environment, ct);
        DateTimeOffset? retryAt = outcome.Verdict == CredentialTestVerdict.Refused
            ? _brake.Remember(tenantId, test.Adapter, environment, outcome.Reason)
            : null;
        return Tested(outcome, retryAt, test.Adapter, environment, fromBrake: false);
    }

    private static CredentialTestReply Tested(
        CredentialTestOutcome outcome, DateTimeOffset? retryAt, string adapter, string? environment, bool fromBrake)
    {
        string message = outcome.Verdict switch
        {
            CredentialTestVerdict.Worked => WorkedMessage,
            CredentialTestVerdict.Unavailable => UnavailableMessage,
            _ => RefusedMessage,   // a recusa e a credencial incompleta: o detalhe diz qual, no log
        };

        return new CredentialTestReply(
            CredentialTestStatus.Tested,
            new CredentialTestResult(outcome.Verdict == CredentialTestVerdict.Worked, message, retryAt),
            Log: new CredentialTestLog(adapter, environment, outcome.Verdict, outcome.Reason, fromBrake));
    }
}
