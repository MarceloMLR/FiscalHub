using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// O teste da credencial gravada da Avalara (change <c>module-navigation-and-integration-panel</c>, D10): a credencial da
/// conta de plataforma, no endpoint de token da conta e no ambiente da instância — que não é escolha do teste (change
/// <c>platform-account-and-connectors</c>, D6 e D11). É o teste da conta, e não de um conector. Troca um token novo e
/// para aí: o contrato verificado no sandbox só tem o envio e a consulta, e os dois são sobre um documento. Uma leitura
/// inventada daria um "funciona" que ninguém verificou.
/// </summary>
internal sealed class AvalaraCredentialTest(IAvalaraTokenProvider tokens, IInstanceEnvironment instance) : IConnectorCredentialTest
{
    public string Adapter => AvalaraServiceCollectionExtensions.AdapterName;

    public ConnectorSettingsKind Side => ConnectorSettingsKind.Platform;

    public Task<CredentialTestOutcome> TestAsync(TenantConnectorProfile profile, CancellationToken ct = default)
        => tokens.ProbeAsync(AvalaraOutboundSettings.Read(profile.TenantId, profile, instance), ct);
}
