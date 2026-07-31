using FiscalHub.Application.Admin;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Admin;

/// <summary>Cadastro do tenant corrente em SQL. Cria sob demanda se ainda não existe registro.</summary>
internal sealed class SqlTenantAdminService : ITenantAdminService
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;

    public SqlTenantAdminService(ProcessingDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<TenantView?> GetAsync(string tenantId, CancellationToken ct = default)
    {
        TenantRow? row = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        return row is null ? null : new TenantView(row.TenantId, row.Name, row.Cnpj, row.Active);
    }

    public async Task<AdminResult<TenantView>> UpdateAsync(string tenantId, UpdateTenantInput input, CancellationToken ct = default)
    {
        string name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return AdminResult<TenantView>.Fail(AdminStatus.Invalid, "Nome é obrigatório.");
        }

        TenantRow? row = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (row is null)
        {
            // Sem registro ainda: cria com o tenant corrente (onboarding tardio do cadastro).
            row = new TenantRow { TenantId = tenantId, Name = name, CreatedAt = _clock.GetUtcNow() };
            _db.Tenants.Add(row);
        }
        else
        {
            row.Name = name;
        }

        row.Cnpj = string.IsNullOrWhiteSpace(input.Cnpj) ? null : input.Cnpj.Trim();
        await _db.SaveChangesAsync(ct);

        return AdminResult<TenantView>.Success(new TenantView(row.TenantId, row.Name, row.Cnpj, row.Active));
    }
}
