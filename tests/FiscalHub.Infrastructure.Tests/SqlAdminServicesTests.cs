using FiscalHub.Application.Admin;
using FiscalHub.Infrastructure.Admin;
using FiscalHub.Infrastructure.Auth;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica a administração de usuários e o cadastro do tenant: criação com validação, papéis,
/// ativar/desativar, reset de senha, isolamento por tenant — e a trava de login para inativo.
/// </summary>
public class SqlAdminServicesTests
{
    [Fact]
    public async Task Creates_lists_and_scopes_users_by_tenant()
    {
        using var h = New();
        var svc = new SqlUserAdminService(h.Db);

        await svc.CreateAsync("tenant-a", new CreateUserInput("ana@a.com", "Ana", "Admin", "Senha123"));
        await svc.CreateAsync("tenant-a", new CreateUserInput("bob@a.com", "Bob", "Viewer", "Senha123"));
        await svc.CreateAsync("tenant-b", new CreateUserInput("beto@b.com", "Beto", "Viewer", "Senha123"));

        IReadOnlyList<AdminUserView> a = await svc.ListAsync("tenant-a");
        Assert.Equal(2, a.Count);                                   // só os do tenant-a
        Assert.DoesNotContain(a, u => u.Email == "beto@b.com");
        Assert.All(a, u => Assert.True(u.Active));
    }

    [Fact]
    public async Task Rejects_duplicate_email_and_invalid_input()
    {
        using var h = New();
        var svc = new SqlUserAdminService(h.Db);
        await svc.CreateAsync("tenant-a", new CreateUserInput("dup@a.com", "Dup", "Viewer", "Senha123"));

        Assert.Equal(AdminStatus.Conflict, (await svc.CreateAsync("tenant-a", new CreateUserInput("dup@a.com", "Outro", "Viewer", "Senha123"))).Status);
        Assert.Equal(AdminStatus.Invalid, (await svc.CreateAsync("tenant-a", new CreateUserInput("sem-arroba", "X", "Viewer", "Senha123"))).Status);
        Assert.Equal(AdminStatus.Invalid, (await svc.CreateAsync("tenant-a", new CreateUserInput("papel@a.com", "X", "Chefe", "Senha123"))).Status);
        Assert.Equal(AdminStatus.Invalid, (await svc.CreateAsync("tenant-a", new CreateUserInput("curta@a.com", "X", "Viewer", "123"))).Status);
    }

    [Fact]
    public async Task Updates_role_and_active_and_resets_password_scoped_to_tenant()
    {
        using var h = New();
        var svc = new SqlUserAdminService(h.Db);
        int id = (await svc.CreateAsync("tenant-a", new CreateUserInput("u@a.com", "U", "Viewer", "Senha123"))).Value!.Id;

        AdminUserView updated = (await svc.UpdateAsync("tenant-a", id, new UpdateUserInput(null, "Admin", false))).Value!;
        Assert.Equal("Admin", updated.Role);
        Assert.False(updated.Active);

        Assert.Equal(AdminStatus.Ok, await svc.ResetPasswordAsync("tenant-a", id, "NovaSenha1"));
        Assert.Equal(AdminStatus.Invalid, await svc.ResetPasswordAsync("tenant-a", id, "123"));      // curta
        // Isolamento: outro tenant não enxerga/edita este usuário.
        Assert.Equal(AdminStatus.NotFound, await svc.ResetPasswordAsync("tenant-b", id, "NovaSenha1"));
        Assert.Equal(AdminStatus.NotFound, (await svc.UpdateAsync("tenant-b", id, new UpdateUserInput(null, "Viewer", null))).Status);
    }

    [Fact]
    public async Task Inactive_user_cannot_authenticate()
    {
        using var h = New();
        h.Db.Users.Add(new UserRow
        {
            Email = "inativo@a.com",
            Name = "Inativo",
            PasswordHash = Pbkdf2PasswordHasher.Hash("Senha123"),
            TenantId = "tenant-a",
            Role = "Viewer",
            Active = false,
        });
        await h.Db.SaveChangesAsync();

        var auth = new SqlUserAuthenticator(h.Db);
        Assert.Null(await auth.AuthenticateAsync("inativo@a.com", "Senha123"));   // inativo é recusado
    }

    [Fact]
    public async Task Tenant_record_is_created_on_first_update_and_then_edited()
    {
        using var h = New();
        var svc = new SqlTenantAdminService(h.Db, TimeProvider.System);

        Assert.Null(await svc.GetAsync("tenant-a"));   // ainda não há registro

        await svc.UpdateAsync("tenant-a", new UpdateTenantInput("ACME Ltda", "12.345.678/0001-90"));
        TenantView? created = await svc.GetAsync("tenant-a");
        Assert.Equal("ACME Ltda", created!.Name);
        Assert.Equal("12.345.678/0001-90", created.Cnpj);

        await svc.UpdateAsync("tenant-a", new UpdateTenantInput("ACME S.A.", null));
        TenantView? updated = await svc.GetAsync("tenant-a");
        Assert.Equal("ACME S.A.", updated!.Name);
        Assert.Null(updated.Cnpj);
        Assert.Equal(1, await h.Db.Tenants.CountAsync());   // um registro por tenant
    }

    private static Harness New()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        DbContextOptions<ProcessingDbContext> options = new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(conn).Options;
        var db = new ProcessingDbContext(options);
        db.Database.EnsureCreated();
        return new Harness(db, conn);
    }

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn) : IDisposable
    {
        public ProcessingDbContext Db => db;

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
