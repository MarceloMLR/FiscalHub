using FiscalHub.Application.Admin;
using FiscalHub.Infrastructure.Auth;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Admin;

/// <summary>Gestão de usuários em SQL, sempre filtrando por tenant (isolamento no nível da query).</summary>
internal sealed class SqlUserAdminService : IUserAdminService
{
    // Papéis aceitos no sistema: Admin (gerencia) e Viewer (só leitura).
    private static readonly string[] Roles = ["Admin", "Viewer"];

    private readonly ProcessingDbContext _db;

    public SqlUserAdminService(ProcessingDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminUserView>> ListAsync(string tenantId, CancellationToken ct = default)
    {
        return await _db.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Name)
            .Select(u => new AdminUserView(u.Id, u.Email, u.Name, u.Role, u.Active))
            .ToListAsync(ct);
    }

    public async Task<AdminResult<AdminUserView>> CreateAsync(string tenantId, CreateUserInput input, CancellationToken ct = default)
    {
        string email = (input.Email ?? string.Empty).Trim().ToLowerInvariant();
        string name = (input.Name ?? string.Empty).Trim();

        if (email.Length == 0 || !email.Contains('@'))
        {
            return AdminResult<AdminUserView>.Fail(AdminStatus.Invalid, "E-mail inválido.");
        }
        if (name.Length == 0)
        {
            return AdminResult<AdminUserView>.Fail(AdminStatus.Invalid, "Nome é obrigatório.");
        }
        if (!Roles.Contains(input.Role))
        {
            return AdminResult<AdminUserView>.Fail(AdminStatus.Invalid, "Papel inválido.");
        }
        if ((input.Password ?? string.Empty).Length < 6)
        {
            return AdminResult<AdminUserView>.Fail(AdminStatus.Invalid, "A senha precisa ter ao menos 6 caracteres.");
        }
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return AdminResult<AdminUserView>.Fail(AdminStatus.Conflict, "Já existe um usuário com esse e-mail.");
        }

        var row = new UserRow
        {
            Email = email,
            Name = name,
            PasswordHash = Pbkdf2PasswordHasher.Hash(input.Password!),
            TenantId = tenantId,
            Role = input.Role,
            Active = true,
        };
        _db.Users.Add(row);
        await _db.SaveChangesAsync(ct);

        return AdminResult<AdminUserView>.Success(new AdminUserView(row.Id, row.Email, row.Name, row.Role, row.Active));
    }

    public async Task<AdminResult<AdminUserView>> UpdateAsync(string tenantId, int userId, UpdateUserInput input, CancellationToken ct = default)
    {
        UserRow? row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);
        if (row is null)
        {
            return AdminResult<AdminUserView>.Fail(AdminStatus.NotFound, "Usuário não encontrado.");
        }

        if (input.Role is not null)
        {
            if (!Roles.Contains(input.Role))
            {
                return AdminResult<AdminUserView>.Fail(AdminStatus.Invalid, "Papel inválido.");
            }
            row.Role = input.Role;
        }
        if (input.Name is not null)
        {
            string name = input.Name.Trim();
            if (name.Length == 0)
            {
                return AdminResult<AdminUserView>.Fail(AdminStatus.Invalid, "Nome é obrigatório.");
            }
            row.Name = name;
        }
        if (input.Active is not null)
        {
            row.Active = input.Active.Value;
        }

        await _db.SaveChangesAsync(ct);
        return AdminResult<AdminUserView>.Success(new AdminUserView(row.Id, row.Email, row.Name, row.Role, row.Active));
    }

    public async Task<AdminStatus> ResetPasswordAsync(string tenantId, int userId, string newPassword, CancellationToken ct = default)
    {
        if ((newPassword ?? string.Empty).Length < 6)
        {
            return AdminStatus.Invalid;
        }

        UserRow? row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);
        if (row is null)
        {
            return AdminStatus.NotFound;
        }

        row.PasswordHash = Pbkdf2PasswordHasher.Hash(newPassword!);
        row.ResetTokenHash = null;            // invalida qualquer token de redefinição em aberto
        row.ResetTokenExpiresAt = null;
        await _db.SaveChangesAsync(ct);
        return AdminStatus.Ok;
    }
}
