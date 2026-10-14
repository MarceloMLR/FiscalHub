using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>Especifica o store do perfil de conector: grava, lê e faz upsert (um por tenant).</summary>
public class SqlConnectorProfileStoreTests
{
    [Fact]
    public async Task Upserts_and_reads_profile_without_duplicating()
    {
        using var h = NewStore();

        Assert.Null(await h.Store.GetAsync("tenant-a"));   // ainda não existe

        await h.Store.UpsertAsync(Profile());
        TenantConnectorProfile? created = await h.Store.GetAsync("tenant-a");
        Assert.Equal("{}", created!.InboundSettings);

        await h.Store.UpsertAsync(Profile() with { InboundSettings = """{"url":"https://erp/"}""" });   // atualiza o mesmo tenant
        TenantConnectorProfile? updated = await h.Store.GetAsync("tenant-a");
        Assert.Equal("""{"url":"https://erp/"}""", updated!.InboundSettings);
        Assert.Equal(1, await h.Db.ConnectorProfiles.CountAsync());   // upsert: uma linha só
    }

    [Fact]
    public async Task Lists_profiles_by_inbound_adapter_across_tenants()
    {
        using var h = NewStore();
        await h.Store.UpsertAsync(Profile() with { TenantId = "tenant-c" });
        await h.Store.UpsertAsync(Profile());   // tenant-a, Dynamics365
        await h.Store.UpsertAsync(Profile() with { TenantId = "tenant-b", InboundAdapter = "iScala" });

        IReadOnlyList<TenantConnectorProfile> d365 = await h.Store.ListByInboundAdapterAsync("Dynamics365");

        Assert.Equal(["tenant-a", "tenant-c"], d365.Select(p => p.TenantId));   // só o adapter pedido, em ordem
        Assert.Empty(await h.Store.ListByInboundAdapterAsync("Xml"));
    }

    // ---- Módulos e integrações viram linhas sob o conector (change platform-account-and-connectors, 6.2) ----

    [Fact]
    public async Task Modules_and_integrations_of_a_connector_go_and_come_back()
    {
        using var h = NewStore();

        await h.Store.UpsertAsync(Profile() with { Accounts = [Account() with { Connectors = [Wired()] }] });

        PlatformConnector read = Assert.Single(Assert.Single((await h.Store.GetAsync("tenant-a"))!.Accounts).Connectors);
        Assert.Equal(
            ["Fiscal/notas-fiscais=taxcompliance/v2/fiscal/dfe/", "Contabil/plano-de-contas=/100", "Contabil/lancamentos=(nulo)/", "Inventario"],
            Lines(read));
        Assert.Equal(["tenant-a"], await h.Db.Set<ConnectorIntegrationRow>().Select(i => i.TenantId).Distinct().ToListAsync());
        Assert.Equal(["Fiscal", "Contabil", "Inventario"], TenantModules.Of(await h.Store.GetAsync("tenant-a")));
    }

    [Fact]
    public async Task Saving_again_updates_modules_and_integrations_in_place_and_drops_what_left()
    {
        using var h = NewStore();
        await h.Store.UpsertAsync(Profile() with { Accounts = [Account() with { Connectors = [Wired()] }] });
        int invoicesId = (await h.Db.Set<ConnectorIntegrationRow>().SingleAsync(i => i.Key == "notas-fiscais")).Id;

        PlatformConnector changed = Wired() with
        {
            Modules = [new ConnectorModule
            {
                Module = "Fiscal",
                Integrations = [new ConnectorIntegration { Key = "notas-fiscais", Endpoint = "outro/caminho" }],
            }],
        };
        await h.Store.UpsertAsync(Profile() with { Accounts = [Account() with { Connectors = [changed] }] });

        ConnectorIntegrationRow invoices = await h.Db.Set<ConnectorIntegrationRow>().AsNoTracking().SingleAsync();
        Assert.Equal(invoicesId, invoices.Id);   // a mesma linha, atualizada no lugar
        Assert.Equal("outro/caminho", invoices.Endpoint);
        Assert.Equal(["Fiscal"], await h.Db.Set<ConnectorModuleRow>().Select(m => m.Module).ToListAsync());

        await h.Store.UpsertAsync(Profile());   // sem contas: o conector sai, e os módulos e as integrações com ele

        Assert.Equal(0, await h.Db.Set<ConnectorModuleRow>().CountAsync());
        Assert.Equal(0, await h.Db.Set<ConnectorIntegrationRow>().CountAsync());
    }

    [Fact]
    public async Task One_tenant_cannot_have_the_same_integration_twice_even_past_the_validation()
    {
        // A rede de segurança da regra de um conector por integração (D3): o índice em (tenant, chave).
        using var h = NewStore();
        PlatformConnector other = Wired() with { Adapter = "Outro" };

        await Assert.ThrowsAsync<DbUpdateException>(
            () => h.Store.UpsertAsync(Profile() with { Accounts = [Account() with { Connectors = [Wired(), other] }] }));
    }

    // Um conector com o Fiscal e a integração de notas com caminho, o Contábil com uma integração vazia e com lote e outra à
    // espera do host, e o Inventário ligado sem integração.
    private static PlatformConnector Wired() => Account().Connectors[0] with
    {
        Modules =
        [
            new ConnectorModule
            {
                Module = "Fiscal",
                Integrations = [new ConnectorIntegration { Key = "notas-fiscais", Endpoint = "taxcompliance/v2/fiscal/dfe" }],
            },
            new ConnectorModule
            {
                Module = "Contabil",
                Integrations =
                [
                    new ConnectorIntegration { Key = "plano-de-contas", Endpoint = "", BatchSize = 100 },
                    new ConnectorIntegration { Key = "lancamentos", Endpoint = null },
                ],
            },
            new ConnectorModule { Module = "Inventario" },
        ],
    };

    // "Módulo/integração=caminho/lote", com o nulo escrito, ou só "Módulo" sem integração.
    private static string[] Lines(PlatformConnector connector)
        => [.. connector.Modules.SelectMany(m => m.Integrations.Count == 0
            ? [m.Module]
            : m.Integrations.Select(i => $"{m.Module}/{i.Key}={i.Endpoint ?? "(nulo)"}/{i.BatchSize}"))];

    // ---- A conta de plataforma e os conectores (change platform-account-and-connectors, grupo 4) ----

    [Fact]
    public async Task The_account_and_its_connectors_go_and_come_back()
    {
        using var h = NewStore();

        await h.Store.UpsertAsync(Profile() with { Accounts = [Account()] });

        PlatformAccount read = Assert.Single((await h.Store.GetAsync("tenant-a"))!.Accounts);
        Assert.Equal(("Avalara", "abc", "kv:fh-tenant-a--platform--avalara--clientsecret", "https://login/oauth/token"),
            (read.Platform, read.ClientId, read.ClientSecretRef, read.TokenUrl));
        PlatformConnector connector = Assert.Single(read.Connectors);
        Assert.Equal(("Avalara", """{"baseUrls":{"v2":"https://avalara/"}}"""), (connector.Adapter, connector.Settings));
    }

    [Fact]
    public async Task Saving_again_updates_the_account_in_place_and_drops_what_left_the_profile()
    {
        using var h = NewStore();
        await h.Store.UpsertAsync(Profile() with
        {
            Accounts = [Account() with { Connectors = [.. Account().Connectors, new PlatformConnector { Adapter = "Outro", Settings = "{}" }] }],
        });
        int accountId = (await h.Db.PlatformAccounts.SingleAsync()).Id;

        await h.Store.UpsertAsync(Profile() with { Accounts = [Account() with { ClientId = "xyz" }] });

        PlatformAccountRow row = await h.Db.PlatformAccounts.Include(a => a.Connectors).SingleAsync();
        Assert.Equal(accountId, row.Id);   // a mesma linha, atualizada no lugar
        Assert.Equal("xyz", row.ClientId);
        Assert.Equal(["Avalara"], row.Connectors.Select(c => c.Adapter));
        Assert.Equal(1, await h.Db.Connectors.CountAsync());

        await h.Store.UpsertAsync(Profile());   // sem contas: a conta sai, e os conectores com ela

        Assert.Equal(0, await h.Db.PlatformAccounts.CountAsync());
        Assert.Equal(0, await h.Db.Connectors.CountAsync());
    }

    [Fact]
    public async Task Accounts_of_one_tenant_do_not_reach_another()
    {
        using var h = NewStore();
        await h.Store.UpsertAsync(Profile() with { Accounts = [Account()] });
        await h.Store.UpsertAsync(Profile() with { TenantId = "tenant-c" });

        Assert.Empty((await h.Store.GetAsync("tenant-c"))!.Accounts);
        Assert.Single((await h.Store.ListByInboundAdapterAsync("Dynamics365")).Single(p => p.TenantId == "tenant-a").Accounts);
    }

    private static PlatformAccount Account() => new()
    {
        Platform = "Avalara",
        ClientId = "abc",
        ClientSecretRef = "kv:fh-tenant-a--platform--avalara--clientsecret",
        TokenUrl = "https://login/oauth/token",
        Connectors = [new PlatformConnector { Adapter = "Avalara", Settings = """{"baseUrls":{"v2":"https://avalara/"}}""" }],
    };

    private static TenantConnectorProfile Profile() => new()
    {
        TenantId = "tenant-a",
        InboundAdapter = "Dynamics365",
    };

    private static Harness NewStore()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(conn).Options;
        var db = new ProcessingDbContext(options);
        db.Database.EnsureCreated();
        return new Harness(db, conn, new SqlConnectorProfileStore(db));
    }

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn, SqlConnectorProfileStore store) : IDisposable
    {
        public ProcessingDbContext Db => db;
        public SqlConnectorProfileStore Store => store;

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
