namespace FiscalHub.Application.Connectors;

/// <summary>O veredito de um teste de credencial.</summary>
public enum CredentialTestVerdict
{
    /// <summary>A credencial funcionou agora, com um token novo.</summary>
    Worked,

    /// <summary>A outra ponta respondeu que não: a credencial, o token, a permissão ou a entidade. Entra no freio.</summary>
    Refused,

    /// <summary>A outra ponta não respondeu agora (tempo esgotado, 5xx, 429). Não é sondagem da credencial, e não freia.</summary>
    Unavailable,

    /// <summary>Falta um campo da credencial, ou o segredo no cofre. Nenhuma requisição saiu.</summary>
    Incomplete,
}

/// <summary>
/// O desfecho de um teste: o veredito e o motivo, em texto nosso. O motivo nunca leva o token, o segredo, nem um
/// cabeçalho com valor.
/// </summary>
public sealed record CredentialTestOutcome(CredentialTestVerdict Verdict, string Reason);

/// <summary>
/// O teste da credencial GRAVADA de um adapter (change <c>module-navigation-and-integration-panel</c>, D9): implementado
/// no adapter, que sabe falar com a ponta dele. Cada teste pede um token novo ao emissor, sem reusar cache, porque o botão
/// responde "essa credencial funciona agora" (D10, D11). O freio e a forma da resposta são do
/// <see cref="ConnectorCredentialTestService"/>, iguais para qualquer adapter.
/// </summary>
public interface IConnectorCredentialTest
{
    /// <summary>O nome do adapter, como o perfil o grava (ex.: "Dynamics365", "Avalara").</summary>
    string Adapter { get; }

    /// <summary>O lado do perfil em que o adapter fica: entrada ou saída.</summary>
    ConnectorSettingsKind Side { get; }

    /// <summary>
    /// Testa a credencial gravada no perfil. <paramref name="environment"/> é o da seção na saída (<c>Sandbox</c> ou
    /// <c>Production</c>), e nulo na entrada.
    /// </summary>
    Task<CredentialTestOutcome> TestAsync(TenantConnectorProfile profile, string? environment, CancellationToken ct = default);
}
