using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>
/// Fábrica de design-time: deixa o <c>dotnet ef</c> construir o contexto (SQL Server) sem subir o
/// host. A connection string aqui é só de dev local, usada pela CLI ao gerar/aplicar migrations —
/// em runtime, quem configura o provider é o composition root.
/// </summary>
internal sealed class ProcessingDbContextFactory : IDesignTimeDbContextFactory<ProcessingDbContext>
{
    public ProcessingDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<ProcessingDbContext> options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=FiscalHub;User Id=sa;Password=Local_Dev_123!;TrustServerCertificate=true")
            .Options;

        return new ProcessingDbContext(options);
    }
}
