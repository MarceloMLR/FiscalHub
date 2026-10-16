using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Outbound;

/// <summary>
/// A chave de envio lida do perfil do tenant, que é onde ela mora — coluna própria em <c>ConnectorProfiles</c>, e não
/// dentro de JSON de settings (ADR-0040). Fica na Application, sobre a porta do perfil, para a esteira não ganhar
/// dependência de Infrastructure e para a leitura ser testável sem banco.
/// </summary>
public sealed class OutboundDispatchSwitch(IConnectorProfileStore profiles) : IOutboundDispatchSwitch
{
    /// <inheritdoc/>
    public async Task<bool> IsOnAsync(string tenantId, CancellationToken ct = default)
        // Sem perfil, ligada: é o comportamento de hoje, e um tenant que ainda não configurou nada não deve parar de
        // enviar por causa de uma chave que ninguém desligou.
        => await profiles.GetAsync(tenantId, ct) is not { } profile || profile.OutboundDispatchEnabled;
}
