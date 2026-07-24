using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Contexto EF Core do rastreio de processamento.</summary>
internal sealed class ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedDocument> ProcessedDocuments => Set<ProcessedDocument>();

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
    }
}
