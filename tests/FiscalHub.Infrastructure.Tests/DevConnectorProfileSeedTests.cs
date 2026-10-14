using System.Text.Json.Nodes;
using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica o seed dos perfis de conector de dev (ADR-0027): código versionado não carrega segredo. Nenhum campo
/// de escrita, e toda referência no prefixo do tenant do próprio perfil — o nome que a tela deriva ao gravar.
/// </summary>
public class DevConnectorProfileSeedTests
{
    [Fact]
    public async Task Seeded_profiles_have_no_write_field_and_every_reference_is_in_the_tenant_prefix()
    {
        Seeded seeded = await SeedAsync();

        Assert.NotEmpty(References(seeded));   // o seed tem referências, e todas passaram pela regra
    }

    [Fact]
    public async Task Seeded_connector_settings_have_no_environment_section_and_no_secret_name_carries_one()
    {
        // O ambiente é da instância (change platform-account-and-connectors, D6): as settings do conector têm um nível só, e
        // o nome do segredo não tem segmento de ambiente.
        Seeded seeded = await SeedAsync();

        foreach (ConnectorRow connector in seeded.Accounts.SelectMany(a => a.Connectors))
        {
            JsonObject settings = JsonNode.Parse(connector.Settings)!.AsObject();
            Assert.DoesNotContain(settings, p => p.Key.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                || p.Key.Equals("production", StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(References(seeded), name => name.Contains("--sandbox--") || name.Contains("--production--"));
    }

    [Fact]
    public async Task Every_seeded_tenant_has_the_avalara_account_with_the_mock_token_endpoint_and_the_connector()
    {
        // A credencial é da conta, e o endpoint de token é obrigatório (change platform-account-and-connectors, D11): o
        // seed aponta o do mock, na raiz, e a v2 do conector também na raiz — a versão é rótulo (D4).
        Seeded seeded = await SeedAsync();

        foreach (ConnectorProfileRow row in seeded.Profiles)
        {
            PlatformAccountRow account = Assert.Single(seeded.Accounts, a => a.TenantId == row.TenantId);
            Assert.Equal("Avalara", account.Platform);
            Assert.Equal("http://localhost:5100/oauth/token", account.TokenUrl);
            Assert.Equal($"kv:fh-{row.TenantId}--platform--avalara--clientsecret", account.ClientSecretRef);
            ConnectorRow connector = Assert.Single(account.Connectors);
            Assert.Equal("Avalara", connector.Adapter);
            Assert.Equal("http://localhost:5100/", (string?)JsonNode.Parse(connector.Settings)!["baseUrls"]!["v2"]);
            Assert.DoesNotContain("clientId", connector.Settings);   // a credencial da plataforma não vai no conector
        }
    }

    [Fact]
    public async Task Every_seeded_connector_has_fiscal_on_with_the_invoices_integration_on_the_sandbox_path_the_mock_serves()
    {
        // O seed liga o Fiscal e a integração de notas com o caminho do sandbox (6.14): o envio de dev sai na primeira subida,
        // pelo caminho que o mock atende, e o host não tem nulo para preencher.
        Seeded seeded = await SeedAsync();

        foreach (ConnectorRow connector in seeded.Accounts.SelectMany(a => a.Connectors))
        {
            ConnectorModuleRow fiscal = Assert.Single(connector.Modules);
            Assert.Equal("Fiscal", fiscal.Module);
            ConnectorIntegrationRow invoices = Assert.Single(fiscal.Integrations);
            Assert.Equal(("notas-fiscais", "taxcompliance/v2/fiscal/dfe"), (invoices.Key, invoices.Endpoint));
        }
    }

    private sealed record Seeded(List<ConnectorProfileRow> Profiles, List<PlatformAccountRow> Accounts);

    private static async Task<Seeded> SeedAsync()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        await using ServiceProvider sp = new ServiceCollection()
            .AddDbContext<ProcessingDbContext>(o => o.UseSqlite(conn))
            .BuildServiceProvider();
        await using (AsyncServiceScope scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessingDbContext>().Database.EnsureCreatedAsync();
        }

        await sp.EnsureDevConnectorProfilesAsync();

        await using AsyncServiceScope read = sp.CreateAsyncScope();
        ProcessingDbContext db = read.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        List<ConnectorProfileRow> rows = await db.ConnectorProfiles.ToListAsync();
        List<PlatformAccountRow> accounts = await db.PlatformAccounts
            .Include(a => a.Connectors).ThenInclude(c => c.Modules).ThenInclude(m => m.Integrations).ToListAsync();
        Assert.NotEmpty(rows);
        return new Seeded(rows, accounts);
    }

    private static List<string> References(Seeded seeded)
    {
        var references = new List<string>();
        foreach (ConnectorProfileRow row in seeded.Profiles)
        {
            foreach (string? settings in new[] { row.InboundSettings, row.SupportSettings })
            {
                Walk(JsonNode.Parse(settings ?? "{}"), row.TenantId, references);
            }
        }

        foreach (PlatformAccountRow account in seeded.Accounts)
        {
            Walk(new JsonObject { ["clientSecretRef"] = account.ClientSecretRef }, account.TenantId, references);
            foreach (ConnectorRow connector in account.Connectors)
            {
                Walk(JsonNode.Parse(connector.Settings), account.TenantId, references);
            }
        }

        return references;
    }

    private static void Walk(JsonNode? node, string tenantId, List<string> references)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Walk(item, tenantId, references);
                }

                break;
            case JsonObject obj:
                foreach ((string name, JsonNode? value) in obj)
                {
                    Assert.False(ConnectorSecretFields.IsWriteField(name), $"campo de escrita '{name}' no seed do {tenantId}");
                    if (ConnectorSecretFields.IsReferenceField(name))
                    {
                        string? reference = value?.GetValue<string>();
                        Assert.True(SecretReference.TryParse(reference, out string? secretName), $"referência malformada em '{name}' do {tenantId}");
                        Assert.True(SecretNames.BelongsTo(secretName, tenantId), $"'{secretName}' fora do prefixo do {tenantId}");
                        references.Add(secretName);
                    }
                    else
                    {
                        Walk(value, tenantId, references);
                    }
                }

                break;
        }
    }
}
