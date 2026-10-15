using System.Net;
using System.Text.Json;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Host.Tests;

/// <summary>
/// A árvore de conectores do <c>/info</c> no endpoint de verdade (<c>module-navigation</c>; design D6). O contrato da
/// montagem está no <c>InstanceInfoTests</c>; aqui se prova o que só o endpoint responde: que ela sai para <b>qualquer
/// papel</b> do tenant — ela traz os nomes dos produtos que o próprio tenant contratou, e não credencial, endereço nem
/// segredo (S8) — e que é a do <b>tenant de quem está logado</b>, e de mais ninguém (ADR-0028).
/// <para>E que o rótulo e o agendamento vêm das declarações registradas no servidor, e não do perfil gravado: o perfil
/// do teste não tem nenhum dos dois.</para>
/// </summary>
public class InfoConnectorsTests
{
    private static readonly IAdapterDeclaration[] Declarations =
    [
        new FakeAdapterDeclaration("Avalara", "Avalara") { Label = "Tax Compliance", Scheduling = true },
        new FakeAdapterDeclaration("Outra", "Outro") { Label = "AvaTax" },
    ];

    [Theory]
    [InlineData("Admin")]
    [InlineData("Viewer")]
    public async Task The_info_answers_the_tree_to_any_role_of_the_tenant(string role)
    {
        await using RoleEndpointHarness harness = await StartAsync();

        JsonElement[] connectors = await ConnectorsAsync(harness, role, "tenant-a");

        JsonElement connector = Assert.Single(connectors);
        Assert.Equal("Avalara", connector.GetProperty("adapter").GetString());
        Assert.Equal("Tax Compliance", connector.GetProperty("label").GetString());
        Assert.True(connector.GetProperty("scheduling").GetBoolean());
        Assert.Equal(["Fiscal"], connector.GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public async Task The_tree_is_the_one_of_the_tenant_of_the_logged_user()
    {
        await using RoleEndpointHarness harness = await StartAsync();

        JsonElement[] a = await ConnectorsAsync(harness, "Viewer", "tenant-a");
        JsonElement[] b = await ConnectorsAsync(harness, "Admin", "tenant-b");

        Assert.Equal(["Tax Compliance"], a.Select(c => c.GetProperty("label").GetString()));
        Assert.Equal(["AvaTax"], b.Select(c => c.GetProperty("label").GetString()));
        Assert.Equal(["Contabil"], b.Single().GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public async Task A_tenant_without_a_profile_gets_the_empty_tree()
    {
        await using RoleEndpointHarness harness = await StartAsync();

        Assert.Empty(await ConnectorsAsync(harness, "Viewer", "tenant-sem-perfil"));
    }

    [Fact]
    public async Task The_response_brings_no_module_list_of_the_tenant()
    {
        await using RoleEndpointHarness harness = await StartAsync();

        using JsonDocument json = await InfoAsync(harness, "Admin", "tenant-a");

        Assert.False(json.RootElement.TryGetProperty("modules", out _));
    }

    private static Task<RoleEndpointHarness> StartAsync() => RoleEndpointHarness.StartAsync(
        profiles:
        [
            Profile("tenant-a", "Avalara", "Avalara", TenantModules.Fiscal),
            Profile("tenant-b", "Outra", "Outro", TenantModules.Accounting),
        ],
        declarations: Declarations);

    private static TenantConnectorProfile Profile(string tenantId, string platform, string adapter, params string[] modules) => new()
    {
        TenantId = tenantId,
        InboundAdapter = "Dynamics365",
        Accounts =
        [
            new PlatformAccount
            {
                Platform = platform,
                Connectors = [new PlatformConnector { Adapter = adapter, Modules = [.. modules.Select(m => new ConnectorModule { Module = m })] }],
            },
        ],
    };

    // Clonados: o JsonDocument é descartado aqui, e um JsonElement dele não sobreviveria ao descarte.
    private static async Task<JsonElement[]> ConnectorsAsync(RoleEndpointHarness harness, string role, string tenantId)
    {
        using JsonDocument json = await InfoAsync(harness, role, tenantId);
        return [.. json.RootElement.GetProperty("connectors").EnumerateArray().Select(c => c.Clone())];
    }

    private static async Task<JsonDocument> InfoAsync(RoleEndpointHarness harness, string role, string tenantId)
    {
        HttpResponseMessage response = await harness.As(role, tenantId).GetAsync("/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
