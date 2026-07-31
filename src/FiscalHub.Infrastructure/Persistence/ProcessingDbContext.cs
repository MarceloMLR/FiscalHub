using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Contexto EF Core do rastreio de processamento.</summary>
internal sealed class ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedDocument> ProcessedDocuments => Set<ProcessedDocument>();

    public DbSet<IntegrationExecutionRow> IntegrationExecutions => Set<IntegrationExecutionRow>();

    public DbSet<ScheduledIntegrationRow> ScheduledIntegrations => Set<ScheduledIntegrationRow>();

    public DbSet<UserRow> Users => Set<UserRow>();

    public DbSet<ConnectorProfileRow> ConnectorProfiles => Set<ConnectorProfileRow>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var doc = modelBuilder.Entity<ProcessedDocument>();
        doc.ToTable("ProcessedDocuments");
        doc.HasKey(d => d.Id);

        // Idempotência no nível do banco: o mesmo documento não entra duas vezes.
        doc.HasIndex(d => new { d.TenantId, d.NaturalKey }).IsUnique();

        doc.Property(d => d.TenantId).HasMaxLength(100);
        doc.Property(d => d.NaturalKey).HasMaxLength(64);
        doc.Property(d => d.ExternalId).HasMaxLength(100);

        // Enums como texto: legíveis direto no banco (auditoria/consulta).
        doc.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
        doc.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);

        doc.Property(d => d.CompanyCode).HasMaxLength(20);
        doc.Property(d => d.BranchCode).HasMaxLength(10);
        doc.Property(d => d.ReferenceDate).HasMaxLength(10);
        doc.Property(d => d.DocumentNumber).HasMaxLength(20);
        doc.Property(d => d.DocumentModel).HasMaxLength(5);
        doc.Property(d => d.Trigger).HasMaxLength(20);

        var exec = modelBuilder.Entity<IntegrationExecutionRow>();
        exec.ToTable("IntegrationExecutions");
        exec.HasKey(e => e.Id);
        exec.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        exec.Property(e => e.TenantId).HasMaxLength(100);
        exec.Property(e => e.CompanyCode).HasMaxLength(20);
        exec.Property(e => e.BranchCode).HasMaxLength(10);
        exec.Property(e => e.PeriodStart).HasMaxLength(10);
        exec.Property(e => e.PeriodEnd).HasMaxLength(10);

        var sched = modelBuilder.Entity<ScheduledIntegrationRow>();
        sched.ToTable("ScheduledIntegrations");
        sched.HasKey(s => s.Id);
        sched.Property(s => s.Mode).HasConversion<string>().HasMaxLength(20);
        sched.Property(s => s.TenantId).HasMaxLength(100);
        sched.Property(s => s.CompanyCode).HasMaxLength(20);
        sched.Property(s => s.BranchCode).HasMaxLength(10);
        sched.Property(s => s.PeriodStart).HasMaxLength(10);
        sched.Property(s => s.PeriodEnd).HasMaxLength(10);

        var usr = modelBuilder.Entity<UserRow>();
        usr.ToTable("Users");
        usr.HasKey(u => u.Id);
        usr.HasIndex(u => u.Email).IsUnique();   // e-mail é a identidade de login
        usr.Property(u => u.Email).HasMaxLength(200);
        usr.Property(u => u.Name).HasMaxLength(200);
        usr.Property(u => u.PasswordHash).HasMaxLength(400);
        usr.Property(u => u.TenantId).HasMaxLength(100);
        usr.Property(u => u.Role).HasMaxLength(50);
        usr.Property(u => u.ResetTokenHash).HasMaxLength(64);

        var conn = modelBuilder.Entity<ConnectorProfileRow>();
        conn.ToTable("ConnectorProfiles");
        conn.HasKey(c => c.Id);
        conn.HasIndex(c => c.TenantId).IsUnique();   // um perfil por tenant
        conn.Property(c => c.TenantId).HasMaxLength(100);
        conn.Property(c => c.Environment).HasMaxLength(20);
        conn.Property(c => c.InboundAdapter).HasMaxLength(50);
        conn.Property(c => c.OutboundAdapter).HasMaxLength(50);

        var tnt = modelBuilder.Entity<TenantRow>();
        tnt.ToTable("Tenants");
        tnt.HasKey(t => t.Id);
        tnt.HasIndex(t => t.TenantId).IsUnique();   // um registro por tenant
        tnt.Property(t => t.TenantId).HasMaxLength(100);
        tnt.Property(t => t.Name).HasMaxLength(200);
        tnt.Property(t => t.Cnpj).HasMaxLength(18);
    }
}
