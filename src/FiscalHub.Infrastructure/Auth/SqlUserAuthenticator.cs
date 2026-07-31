using FiscalHub.Application.Auth;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Auth;

/// <summary>Autentica contra a tabela Users: acha por e-mail e confere a senha pelo hash PBKDF2.</summary>
internal sealed class SqlUserAuthenticator : IUserAuthenticator
{
    private readonly ProcessingDbContext _db;

    public SqlUserAuthenticator(ProcessingDbContext db) => _db = db;

    public async Task<AppUser?> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        UserRow? row = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (row is null || !row.Active || !Pbkdf2PasswordHasher.Verify(row.PasswordHash, password))
        {
            return null;   // inexistente, inativo ou senha errada — mesma resposta, sem vazar qual dos três
        }

        return new AppUser
        {
            Id = row.Id,
            Email = row.Email,
            Name = row.Name,
            TenantId = row.TenantId,
            Role = row.Role,
        };
    }
}
