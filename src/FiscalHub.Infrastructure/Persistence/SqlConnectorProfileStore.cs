using System.Text.Json;
using FiscalHub.Application.Connectors;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Implementação de <see cref="IConnectorProfileStore"/> em EF Core.</summary>
internal sealed class SqlConnectorProfileStore : IConnectorProfileStore
{
    private readonly ProcessingDbContext _db;

    public SqlConnectorProfileStore(ProcessingDbContext db) => _db = db;

    public async Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
    {
        ConnectorProfileRow? row = await _db.ConnectorProfiles.FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        return row is null ? null : Map(row);
    }

    public async Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default)
    {
        ConnectorProfileRow? row = await _db.ConnectorProfiles.FirstOrDefaultAsync(p => p.TenantId == profile.TenantId, ct);

        if (row is null)
        {
            _db.ConnectorProfiles.Add(new ConnectorProfileRow
            {
                TenantId = profile.TenantId,
                Environment = profile.Environment,
                InboundAdapter = profile.InboundAdapter,
                InboundSettings = profile.InboundSettings,
                OutboundAdapter = profile.OutboundAdapter,
                OutboundSettings = profile.OutboundSettings,
                SupportAdapter = profile.SupportAdapter,
                SupportSettings = profile.SupportSettings,
                Modules = WriteModules(profile.Modules),
            });
        }
        else
        {
            row.Environment = profile.Environment;
            row.InboundAdapter = profile.InboundAdapter;
            row.InboundSettings = profile.InboundSettings;
            row.OutboundAdapter = profile.OutboundAdapter;
            row.OutboundSettings = profile.OutboundSettings;
            row.SupportAdapter = profile.SupportAdapter;
            row.SupportSettings = profile.SupportSettings;
            row.Modules = WriteModules(profile.Modules);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
    {
        // Consulta de sistema: varre todos os tenants (o worker de poll não tem tenant logado).
        List<ConnectorProfileRow> rows = await _db.ConnectorProfiles
            .Where(p => p.InboundAdapter == inboundAdapter)
            .OrderBy(p => p.TenantId)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    private static TenantConnectorProfile Map(ConnectorProfileRow r) => new()
    {
        TenantId = r.TenantId,
        Environment = r.Environment,
        InboundAdapter = r.InboundAdapter,
        InboundSettings = r.InboundSettings,
        OutboundAdapter = r.OutboundAdapter,
        OutboundSettings = r.OutboundSettings,
        SupportAdapter = r.SupportAdapter,
        SupportSettings = r.SupportSettings ?? "{}",
        Modules = ReadModules(r.Modules),
    };

    private static string? WriteModules(IReadOnlyList<string>? modules)
        => modules is null ? null : JsonSerializer.Serialize(modules);

    // A coluna é apresentação (D2): um valor ilegível ou fora dos aceitos, posto por SQL, cai no padrão em vez de derrubar a
    // leitura do perfil. O próximo salvar pela tela regrava a lista normalizada.
    private static IReadOnlyList<string>? ReadModules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            string[]? values = JsonSerializer.Deserialize<string[]>(json);
            return values is not null && TenantModules.TryNormalize(values, out IReadOnlyList<string> modules, out _) ? modules : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
