namespace FiscalHub.Application.Connectors;

/// <summary>
/// Quem guarda algo derivado do perfil de conector de um tenant, e precisa esquecê-lo quando o perfil ou um segredo
/// dele muda: o token em cache e a recusa lembrada do adapter de saída (ADR-0027). O
/// <see cref="ConnectorProfileService"/> avisa depois de qualquer escrita no cofre ou no perfil.
/// </summary>
public interface IConnectorProfileObserver
{
    Task ProfileSavedAsync(string tenantId, CancellationToken ct = default);
}
