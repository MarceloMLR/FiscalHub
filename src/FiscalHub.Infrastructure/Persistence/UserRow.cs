namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Linha de persistência de um usuário. A senha nunca é guardada em claro — só o hash.</summary>
internal sealed class UserRow
{
    public int Id { get; set; }

    public required string Email { get; set; }

    public required string Name { get; set; }

    public required string PasswordHash { get; set; }

    public required string TenantId { get; set; }

    public required string Role { get; set; }

    /// <summary>Usuário ativo pode logar; inativo é recusado na autenticação (desativação reversível).</summary>
    public bool Active { get; set; } = true;

    /// <summary>Hash (SHA-256) do token de redefinição de senha em aberto; <c>null</c> quando não há.</summary>
    public string? ResetTokenHash { get; set; }

    /// <summary>Validade do token de redefinição (uso único, expira em minutos).</summary>
    public DateTimeOffset? ResetTokenExpiresAt { get; set; }
}
