namespace FiscalHub.Application.Auth;

/// <summary>Valida credenciais (e-mail + senha) contra o armazém de usuários. Implementada na Infrastructure.</summary>
public interface IUserAuthenticator
{
    /// <summary>Devolve o usuário se as credenciais conferem; <c>null</c> caso contrário.</summary>
    Task<AppUser?> AuthenticateAsync(string email, string password, CancellationToken ct = default);
}
