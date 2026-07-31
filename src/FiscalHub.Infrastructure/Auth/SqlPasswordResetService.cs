using System.Security.Cryptography;
using System.Text;
using FiscalHub.Application.Auth;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Auth;

/// <summary>
/// Redefinição de senha sobre a tabela Users: guarda só o HASH do token (nunca o token cru) com
/// validade curta, e ao consumir troca a senha (PBKDF2) e invalida o token (uso único).
/// </summary>
internal sealed class SqlPasswordResetService : IPasswordResetService
{
    private const int ValidityMinutes = 30;

    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;

    public SqlPasswordResetService(ProcessingDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string?> RequestResetAsync(string email, CancellationToken ct = default)
    {
        UserRow? row = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (row is null)
        {
            return null;   // e-mail não existe — o Host responde igual, sem vazar
        }

        string token = Base64Url(RandomNumberGenerator.GetBytes(32));
        row.ResetTokenHash = Sha256Hex(token);
        row.ResetTokenExpiresAt = _clock.GetUtcNow().AddMinutes(ValidityMinutes);
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> ResetAsync(string token, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
        {
            return false;
        }

        string hash = Sha256Hex(token);
        UserRow? row = await _db.Users.FirstOrDefaultAsync(u => u.ResetTokenHash == hash, ct);
        if (row is null || row.ResetTokenExpiresAt is null || row.ResetTokenExpiresAt < _clock.GetUtcNow())
        {
            return false;
        }

        row.PasswordHash = Pbkdf2PasswordHasher.Hash(newPassword);
        row.ResetTokenHash = null;
        row.ResetTokenExpiresAt = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
