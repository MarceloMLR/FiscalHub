namespace FiscalHub.Application.Auth;

/// <summary>
/// Fluxo de "esqueci a senha": gera um token de redefinição (uso único, com validade) e depois o
/// consome para trocar a senha. A entrega do token (e-mail) fica fora daqui — em dev, o Host devolve
/// o token na resposta pra completar o fluxo sem servidor de e-mail.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Gera e guarda o hash de um token de redefinição para o e-mail, se ele existir. Retorna o token
    /// cru (para entrega) ou <c>null</c> se não há conta — o chamador deve responder igual nos dois casos.
    /// </summary>
    Task<string?> RequestResetAsync(string email, CancellationToken ct = default);

    /// <summary>Consome o token e troca a senha. <c>false</c> se o token é inválido ou expirou.</summary>
    Task<bool> ResetAsync(string token, string newPassword, CancellationToken ct = default);
}
