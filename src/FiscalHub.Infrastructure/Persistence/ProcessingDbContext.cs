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

    public DbSet<PlatformAccountRow> PlatformAccounts => Set<PlatformAccountRow>();

    public DbSet<ConnectorRow> Connectors => Set<ConnectorRow>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<ChangeFeedCursorRow> ChangeFeedCursors => Set<ChangeFeedCursorRow>();

    public DbSet<LeaseRow> Leases => Set<LeaseRow>();

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
        doc.Property(d => d.BranchCode).HasMaxLength(20);
        doc.Property(d => d.ReferenceDate).HasMaxLength(10);
        doc.Property(d => d.ExecutedOn).HasMaxLength(10);
        doc.Property(d => d.PeriodStart).HasMaxLength(10);
        doc.Property(d => d.PeriodEnd).HasMaxLength(10);
        doc.Property(d => d.DocumentNumber).HasMaxLength(20);
        doc.Property(d => d.DocumentModel).HasMaxLength(5);
        doc.Property(d => d.Trigger).HasMaxLength(20);

        var exec = modelBuilder.Entity<IntegrationExecutionRow>();
        exec.ToTable("IntegrationExecutions");
        exec.HasKey(e => e.Id);
        exec.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        exec.Property(e => e.TenantId).HasMaxLength(100);
        exec.Property(e => e.CompanyCode).HasMaxLength(20);
        exec.Property(e => e.BranchCode).HasMaxLength(20);
        exec.Property(e => e.EstablishmentTaxId).HasMaxLength(20);   // o CNPJ normalizado tem 14; anulável, só na execução
        exec.Property(e => e.PeriodStart).HasMaxLength(10);
        exec.Property(e => e.PeriodEnd).HasMaxLength(10);

        var sched = modelBuilder.Entity<ScheduledIntegrationRow>();
        sched.ToTable("ScheduledIntegrations");
        sched.HasKey(s => s.Id);
        sched.Property(s => s.Mode).HasConversion<string>().HasMaxLength(20);
        sched.Property(s => s.TenantId).HasMaxLength(100);
        sched.Property(s => s.CompanyCode).HasMaxLength(20);
        sched.Property(s => s.BranchCode).HasMaxLength(20);
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
        conn.Property(c => c.InboundAdapter).HasMaxLength(50);
        conn.Property(c => c.SupportAdapter).HasMaxLength(50);

        // A conta de plataforma e os conectores dela (ADR-0036): uma conta por plataforma, por tenant, e um conector por
        // adapter, dentro da conta. A validação da gravação é a regra; os índices são a rede de segurança.
        var account = modelBuilder.Entity<PlatformAccountRow>();
        account.ToTable("PlatformAccounts");
        account.HasKey(a => a.Id);
        account.HasIndex(a => new { a.TenantId, a.Platform }).IsUnique();
        account.Property(a => a.TenantId).HasMaxLength(100);
        account.Property(a => a.Platform).HasMaxLength(50);
        account.Property(a => a.ClientId).HasMaxLength(200);
        account.Property(a => a.ClientSecretRef).HasMaxLength(200);   // kv: e um nome de até 127 caracteres
        account.Property(a => a.TokenUrl).HasMaxLength(500);
        account.HasMany(a => a.Connectors).WithOne().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);

        var connector = modelBuilder.Entity<ConnectorRow>();
        connector.ToTable("Connectors");
        connector.HasKey(c => c.Id);
        connector.HasIndex(c => new { c.AccountId, c.Adapter }).IsUnique();
        connector.Property(c => c.Adapter).HasMaxLength(50);
        connector.HasMany(c => c.Modules).WithOne().HasForeignKey(m => m.ConnectorId).OnDelete(DeleteBehavior.Cascade);

        // Os módulos e as integrações ligados no conector (D3): o que o perfil liga é dado consultável, e não blob. A regra
        // de um conector por integração, por tenant, é a validação da gravação; o índice em (tenant, chave) é a rede de
        // segurança, e por isso a linha da integração repete o tenant.
        var module = modelBuilder.Entity<ConnectorModuleRow>();
        module.ToTable("ConnectorModules");
        module.HasKey(m => m.Id);
        module.HasIndex(m => new { m.ConnectorId, m.Module }).IsUnique();
        module.Property(m => m.Module).HasMaxLength(50);
        module.HasMany(m => m.Integrations).WithOne().HasForeignKey(i => i.ModuleId).OnDelete(DeleteBehavior.Cascade);

        var integration = modelBuilder.Entity<ConnectorIntegrationRow>();
        integration.ToTable("ConnectorIntegrations");
        integration.HasKey(i => i.Id);
        integration.HasIndex(i => new { i.TenantId, i.Key }).IsUnique();
        integration.Property(i => i.TenantId).HasMaxLength(100);
        integration.Property(i => i.Key).HasMaxLength(50);
        integration.Property(i => i.Endpoint).HasMaxLength(500);

        var tnt = modelBuilder.Entity<TenantRow>();
        tnt.ToTable("Tenants");
        tnt.HasKey(t => t.Id);
        tnt.HasIndex(t => t.TenantId).IsUnique();   // um registro por tenant
        tnt.Property(t => t.TenantId).HasMaxLength(100);
        tnt.Property(t => t.Name).HasMaxLength(200);
        tnt.Property(t => t.Cnpj).HasMaxLength(18);

        var cursor = modelBuilder.Entity<ChangeFeedCursorRow>();
        cursor.ToTable("ChangeFeedCursors");
        cursor.HasKey(c => c.Id);
        cursor.HasIndex(c => new { c.TenantId, c.Origin }).IsUnique();   // uma marca por (tenant, origem)
        cursor.Property(c => c.TenantId).HasMaxLength(100);
        cursor.Property(c => c.Origin).HasMaxLength(50);
        cursor.Property(c => c.LastError).HasMaxLength(500);

        var lease = modelBuilder.Entity<LeaseRow>();
        lease.ToTable("Leases");
        lease.HasKey(l => l.Resource);   // um lease por recurso
        lease.Property(l => l.Resource).HasMaxLength(200);
        lease.Property(l => l.Owner).HasMaxLength(200);
    }
}
