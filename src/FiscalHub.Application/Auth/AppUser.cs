namespace FiscalHub.Application.Auth;

/// <summary>Usuário autenticado — o essencial que vira claim no token e escopo dos dados.</summary>
public sealed record AppUser
{
    public required int Id { get; init; }
    public required string Email { get; init; }
    public required string Name { get; init; }

    /// <summary>Tenant do usuário — usado para escopar os dados que ele enxerga.</summary>
    public required string TenantId { get; init; }

    public required string Role { get; init; }
}
