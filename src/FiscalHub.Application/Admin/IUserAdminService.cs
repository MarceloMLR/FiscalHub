namespace FiscalHub.Application.Admin;

/// <summary>
/// Gestão de usuários escopada a um tenant. Toda operação recebe o tenant corrente e nunca
/// enxerga/altera usuários de outro tenant — o isolamento é responsabilidade da implementação.
/// </summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<AdminUserView>> ListAsync(string tenantId, CancellationToken ct = default);

    Task<AdminResult<AdminUserView>> CreateAsync(string tenantId, CreateUserInput input, CancellationToken ct = default);

    Task<AdminResult<AdminUserView>> UpdateAsync(string tenantId, int userId, UpdateUserInput input, CancellationToken ct = default);

    Task<AdminStatus> ResetPasswordAsync(string tenantId, int userId, string newPassword, CancellationToken ct = default);
}
