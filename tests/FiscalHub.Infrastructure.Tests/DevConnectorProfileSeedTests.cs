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
        List<ConnectorProfileRow> rows = await read.ServiceProvider.GetRequiredService<ProcessingDbContext>().ConnectorProfiles.ToListAsync();
        Assert.NotEmpty(rows);

        var references = new List<string>();
        foreach (ConnectorProfileRow row in rows)
        {
            foreach (string? settings in new[] { row.InboundSettings, row.OutboundSettings, row.SupportSettings })
            {
                Walk(JsonNode.Parse(settings ?? "{}"), row.TenantId, references);
            }
        }

        Assert.NotEmpty(references);   // o seed tem referências, e todas passaram pela regra
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
