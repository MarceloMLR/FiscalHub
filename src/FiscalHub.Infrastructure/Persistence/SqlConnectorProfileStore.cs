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
        return row is null ? null : Map(row, await AccountsOfAsync([tenantId], ct));
    }

    public async Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default)
    {
        ConnectorProfileRow? row = await _db.ConnectorProfiles.FirstOrDefaultAsync(p => p.TenantId == profile.TenantId, ct);

        if (row is null)
        {
            _db.ConnectorProfiles.Add(new ConnectorProfileRow
            {
                TenantId = profile.TenantId,
                InboundAdapter = profile.InboundAdapter,
                InboundSettings = profile.InboundSettings,
                SupportAdapter = profile.SupportAdapter,
                SupportSettings = profile.SupportSettings,
                OutboundDispatchEnabled = profile.OutboundDispatchEnabled,
            });
        }
        else
        {
            row.InboundAdapter = profile.InboundAdapter;
            row.InboundSettings = profile.InboundSettings;
            row.SupportAdapter = profile.SupportAdapter;
            row.SupportSettings = profile.SupportSettings;
            row.OutboundDispatchEnabled = profile.OutboundDispatchEnabled;
        }

        await MergeAccountsAsync(profile, ct);
        await _db.SaveChangesAsync(ct);
    }

    // As contas, os conectores, os módulos e as integrações do perfil substituem os gravados, casando pela chave — a
    // plataforma na conta, o adapter no conector, o nome no módulo e a chave na integração, sem distinguir maiúscula, como
    // os índices únicos. O que sumiu do perfil sai do banco; o resto é atualizado no lugar, e não apagado e recriado.
    private async Task MergeAccountsAsync(TenantConnectorProfile profile, CancellationToken ct)
    {
        List<PlatformAccountRow> stored = await _db.PlatformAccounts
            .Include(a => a.Connectors).ThenInclude(c => c.Modules).ThenInclude(m => m.Integrations)
            .Where(a => a.TenantId == profile.TenantId).ToListAsync(ct);

        foreach (PlatformAccountRow gone in stored.Where(r => !profile.Accounts.Any(a => Same(a.Platform, r.Platform))))
        {
            _db.PlatformAccounts.Remove(gone);
        }

        foreach (PlatformAccount account in profile.Accounts)
        {
            PlatformAccountRow? row = stored.FirstOrDefault(r => Same(r.Platform, account.Platform));
            if (row is null)
            {
                row = new PlatformAccountRow { TenantId = profile.TenantId, Platform = account.Platform };
                _db.PlatformAccounts.Add(row);
            }

            row.Platform = account.Platform;
            row.ClientId = account.ClientId;
            row.ClientSecretRef = account.ClientSecretRef;
            row.TokenUrl = account.TokenUrl;

            foreach (ConnectorRow gone in row.Connectors.Where(c => !account.Connectors.Any(a => Same(a.Adapter, c.Adapter))).ToList())
            {
                row.Connectors.Remove(gone);
                _db.Connectors.Remove(gone);
            }

            foreach (PlatformConnector connector in account.Connectors)
            {
                ConnectorRow? existing = row.Connectors.FirstOrDefault(c => Same(c.Adapter, connector.Adapter));
                if (existing is null)
                {
                    existing = new ConnectorRow { Adapter = connector.Adapter, Settings = connector.Settings };
                    row.Connectors.Add(existing);
                }

                existing.Adapter = connector.Adapter;
                existing.Settings = connector.Settings;
                MergeModules(profile.TenantId, existing, connector.Modules);
            }
        }
    }

    private void MergeModules(string tenantId, ConnectorRow row, IReadOnlyList<ConnectorModule> modules)
    {
        foreach (ConnectorModuleRow gone in row.Modules.Where(r => !modules.Any(m => Same(m.Module, r.Module))).ToList())
        {
            row.Modules.Remove(gone);
            _db.Remove(gone);
        }

        foreach (ConnectorModule module in modules)
        {
            ConnectorModuleRow? existing = row.Modules.FirstOrDefault(r => Same(r.Module, module.Module));
            if (existing is null)
            {
                existing = new ConnectorModuleRow { Module = module.Module };
                row.Modules.Add(existing);
            }

            existing.Module = module.Module;
            foreach (ConnectorIntegrationRow gone in existing.Integrations.Where(r => !module.Integrations.Any(i => Same(i.Key, r.Key))).ToList())
            {
                existing.Integrations.Remove(gone);
                _db.Remove(gone);
            }

            foreach (ConnectorIntegration integration in module.Integrations)
            {
                ConnectorIntegrationRow? current = existing.Integrations.FirstOrDefault(r => Same(r.Key, integration.Key));
                if (current is null)
                {
                    current = new ConnectorIntegrationRow { TenantId = tenantId, Key = integration.Key };
                    existing.Integrations.Add(current);
                }

                current.Key = integration.Key;
                current.Endpoint = integration.Endpoint;
                current.BatchSize = integration.BatchSize;
            }
        }
    }

    private async Task<ILookup<string, PlatformAccountRow>> AccountsOfAsync(IReadOnlyCollection<string> tenants, CancellationToken ct)
        => (await _db.PlatformAccounts.AsNoTracking()
            .Include(a => a.Connectors).ThenInclude(c => c.Modules).ThenInclude(m => m.Integrations)
            .Where(a => tenants.Contains(a.TenantId)).OrderBy(a => a.Id).ToListAsync(ct))
            .ToLookup(a => a.TenantId, StringComparer.Ordinal);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
    {
        // Consulta de sistema: varre todos os tenants (o worker de poll não tem tenant logado).
        List<ConnectorProfileRow> rows = await _db.ConnectorProfiles
            .Where(p => p.InboundAdapter == inboundAdapter)
            .OrderBy(p => p.TenantId)
            .ToListAsync(ct);
        ILookup<string, PlatformAccountRow> accounts = await AccountsOfAsync([.. rows.Select(r => r.TenantId)], ct);
        return [.. rows.Select(r => Map(r, accounts))];
    }

    private static TenantConnectorProfile Map(ConnectorProfileRow r, ILookup<string, PlatformAccountRow> accounts) => new()
    {
        TenantId = r.TenantId,
        InboundAdapter = r.InboundAdapter,
        InboundSettings = r.InboundSettings,
        Accounts = [.. accounts[r.TenantId].Select(a => new PlatformAccount
        {
            Platform = a.Platform,
            ClientId = a.ClientId,
            ClientSecretRef = a.ClientSecretRef,
            TokenUrl = a.TokenUrl,
            Connectors = [.. a.Connectors.OrderBy(c => c.Id).Select(c => new PlatformConnector
            {
                Adapter = c.Adapter,
                Settings = c.Settings,
                Modules = [.. c.Modules.OrderBy(m => Order(m.Module)).ThenBy(m => m.Id).Select(m => new ConnectorModule
                {
                    Module = m.Module,
                    Integrations = [.. m.Integrations.OrderBy(i => i.Id).Select(i => new ConnectorIntegration
                    {
                        Key = i.Key,
                        Endpoint = i.Endpoint,
                        BatchSize = i.BatchSize,
                    })],
                })],
            })],
        })],
        SupportAdapter = r.SupportAdapter,
        SupportSettings = r.SupportSettings ?? "{}",
        OutboundDispatchEnabled = r.OutboundDispatchEnabled,
    };

    // Os módulos na ordem da barra lateral; um nome fora dela, posto por SQL, vai para o fim.
    private static int Order(string module)
    {
        int index = TenantModules.Known.ToList().IndexOf(module);
        return index < 0 ? int.MaxValue : index;
    }
}
