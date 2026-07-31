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
                Realtime = profile.Realtime,
                InboundAdapter = profile.InboundAdapter,
                InboundSettings = profile.InboundSettings,
                OutboundAdapter = profile.OutboundAdapter,
                OutboundSettings = profile.OutboundSettings,
                SupportAdapter = profile.SupportAdapter,
                SupportSettings = profile.SupportSettings,
            });
        }
        else
        {
            row.Environment = profile.Environment;
            row.Realtime = profile.Realtime;
            row.InboundAdapter = profile.InboundAdapter;
            row.InboundSettings = profile.InboundSettings;
            row.OutboundAdapter = profile.OutboundAdapter;
            row.OutboundSettings = profile.OutboundSettings;
            row.SupportAdapter = profile.SupportAdapter;
            row.SupportSettings = profile.SupportSettings;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static TenantConnectorProfile Map(ConnectorProfileRow r) => new()
    {
        TenantId = r.TenantId,
        Environment = r.Environment,
        Realtime = r.Realtime,
        InboundAdapter = r.InboundAdapter,
        InboundSettings = r.InboundSettings,
        OutboundAdapter = r.OutboundAdapter,
        OutboundSettings = r.OutboundSettings,
        SupportAdapter = r.SupportAdapter,
        SupportSettings = r.SupportSettings ?? "{}",
    };
}
