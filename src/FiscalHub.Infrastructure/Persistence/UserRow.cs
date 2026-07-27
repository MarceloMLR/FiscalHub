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
}
