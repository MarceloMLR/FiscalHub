using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// O teste da credencial gravada da Avalara (change <c>module-navigation-and-integration-panel</c>, D10): a seção do
/// ambiente escolhido, e não a do ativo, porque testar a credencial de Produção antes de trocar o ambiente é o caso de uso.
/// Troca um token novo e para aí: o contrato verificado no sandbox só tem o envio e a consulta, e os dois são sobre um
/// documento. Uma leitura inventada daria um "funciona" que ninguém verificou.
/// </summary>
internal sealed class AvalaraCredentialTest(IAvalaraTokenProvider tokens) : IConnectorCredentialTest
{
    public string Adapter => AvalaraServiceCollectionExtensions.AdapterName;

    public ConnectorSettingsKind Side => ConnectorSettingsKind.Outbound;

    public Task<CredentialTestOutcome> TestAsync(TenantConnectorProfile profile, string? environment, CancellationToken ct = default)
        => tokens.ProbeAsync(AvalaraOutboundSettings.Read(profile.TenantId, profile with { Environment = environment ?? profile.Environment }), ct);
}
