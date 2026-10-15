using System.Text.Json;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o contrato do <c>/info</c> (change <c>platform-account-and-connectors</c>, D6): o ambiente é o da instância,
/// e nunca o do perfil nem um padrão.
/// <para>A change <c>connector-navigation-and-admin-json</c> muda duas coisas. O <c>canViewRawJson</c> (D7): a resposta
/// diz, para o usuário logado, se ele pode ver as fotos cruas — quem conhece a lista de papéis é o Host, e por isso ela
/// chega aqui já respondida; a Application não decide permissão, e nem sabe quem está logado. E a <b>árvore de
/// conectores</b> (D6), que troca a lista plana <c>modules</c>: uma entrada por conector do tenant, na ordem gravada no
/// perfil, com o adapter, o rótulo e o agendamento <b>declarados pelo adapter</b> e os módulos ligados naquele conector.
/// A lista plana saiu porque a justificativa dela morreu — era por ela que a barra lateral era montada, e não é mais —, e
/// duas representações da mesma verdade na mesma resposta criariam a pergunta de qual vale.</para>
/// </summary>
public class InstanceInfoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Sandbox")]
    [InlineData("Production")]
    public void The_environment_is_the_instance_one_whatever_the_profile(string environment)
    {
        InstanceInfo info = Info(environment, Profile(Connector("Avalara")));

        Assert.Equal(environment, info.Environment);
    }

    [Fact]
    public void Without_a_profile_the_environment_is_still_the_instance_one()
    {
        InstanceInfo info = Info("Production", null);

        Assert.Equal("Production", info.Environment);
        Assert.False(info.AutomaticIntegration);
        Assert.False(info.InboundScans);
        Assert.Empty(info.Connectors);
    }

    [Fact]
    public void A_tenant_with_a_profile_and_no_connector_gets_the_empty_tree()
    {
        // Não existe conector presumido (D5): o servidor responde a árvore vazia, e o que a barra lateral desenha nesse
        // caso — o grupo que nomeia a ausência, com o Fiscal e o Agendamento — é regra de tela.
        InstanceInfo info = Info("Sandbox", Profile());

        Assert.Empty(info.Connectors);
    }

    [Fact]
    public void A_connector_brings_the_adapter_the_declared_label_and_the_declared_scheduling()
    {
        // O rótulo e o agendamento são da declaração do adapter (platform-account-connectors), e não do perfil: nada
        // disso está gravado no conector.
        InstanceInfo info = Info("Sandbox", Profile(Connector("Avalara", TenantModules.Fiscal)));

        ConnectorInfo connector = Assert.Single(info.Connectors);
        Assert.Equal("Avalara", connector.Adapter);
        Assert.Equal("Tax Compliance", connector.Label);
        Assert.True(connector.Scheduling);
        Assert.Equal(["Fiscal"], connector.Modules);
    }

    [Fact]
    public void A_connector_whose_adapter_does_not_schedule_says_so()
    {
        InstanceInfo info = Info("Sandbox", Profile("Outra", Connector("Outro", TenantModules.Fiscal)));

        ConnectorInfo connector = Assert.Single(info.Connectors);
        Assert.Equal("AvaTax", connector.Label);
        Assert.False(connector.Scheduling);
    }

    [Fact]
    public void The_modules_of_a_connector_come_in_the_sidebar_order_without_repeating()
    {
        // A ordem é a da barra lateral (Fiscal, Contábil, Inventário), e não a gravada no conector; o repetido conta uma
        // vez, e o que não é módulo aceito não entra.
        InstanceInfo info = Info("Sandbox", Profile(
            Connector("Avalara", TenantModules.Inventory, TenantModules.Fiscal, TenantModules.Inventory, "Outro")));

        Assert.Equal(["Fiscal", "Inventario"], Assert.Single(info.Connectors).Modules);
    }

    [Fact]
    public void A_connector_without_a_module_brings_the_empty_list_and_is_still_an_entry()
    {
        // Quem decide que um conector sem item não vira grupo é a tela (module-navigation): o servidor responde o que o
        // tenant tem.
        InstanceInfo info = Info("Sandbox", Profile(Connector("Avalara")));

        Assert.Empty(Assert.Single(info.Connectors).Modules);
    }

    [Fact]
    public void Two_connectors_give_two_entries_in_the_recorded_order()
    {
        // A ordem é a gravada no perfil — a mesma que Configurações mostra (D6) —, e o Fiscal ligado nos dois aparece em
        // cada um: com dois conectores existem dois Fiscais, e eles não se somam numa lista do tenant.
        InstanceInfo info = Info("Sandbox", Profile(
            Connector("Outro", TenantModules.Fiscal),
            Connector("Avalara", TenantModules.Fiscal, TenantModules.Accounting)));

        Assert.Equal(["Outro", "Avalara"], info.Connectors.Select(c => c.Adapter));
        Assert.Equal(["Fiscal"], info.Connectors[0].Modules);
        Assert.Equal(["Fiscal", "Contabil"], info.Connectors[1].Modules);
    }

    [Fact]
    public void The_connectors_of_two_accounts_come_in_the_recorded_order_too()
    {
        TenantConnectorProfile profile = new()
        {
            TenantId = "tenant-a",
            InboundAdapter = "Dynamics365",
            Accounts =
            [
                new PlatformAccount { Platform = "Outra", Connectors = [Connector("Outro", TenantModules.Fiscal)] },
                new PlatformAccount { Platform = "Avalara", Connectors = [Connector("Avalara", TenantModules.Fiscal)] },
            ],
        };

        InstanceInfo info = Info("Sandbox", profile);

        Assert.Equal(["AvaTax", "Tax Compliance"], info.Connectors.Select(c => c.Label));
    }

    [Fact]
    public void A_connector_of_an_adapter_nobody_declares_keeps_the_adapter_as_the_label()
    {
        // A gravação recusa adapter não declarado, e esta é a sobra: um adapter que saiu do deploy, ou uma linha semeada
        // por SQL. O conector não some da resposta — some seria esconder configuração existente —, e o nome dele é o
        // adapter, como a leitura do perfil já faz (ConnectorProfileService.IntegrationsOf tolera a declaração ausente).
        InstanceInfo info = Info("Sandbox", Profile(Connector("Sumido", TenantModules.Fiscal)));

        ConnectorInfo connector = Assert.Single(info.Connectors);
        Assert.Equal("Sumido", connector.Adapter);
        Assert.Equal("Sumido", connector.Label);
        Assert.False(connector.Scheduling);
    }

    [Fact]
    public void A_declaration_of_another_platform_does_not_name_the_connector()
    {
        // A identidade do conector é o par (plataforma, adapter), comparado exato (D17): um adapter de mesmo nome noutra
        // plataforma não empresta o rótulo dele.
        InstanceInfo info = Info("Sandbox", Profile("Outra", Connector("Avalara", TenantModules.Fiscal)));

        Assert.Equal("Avalara", Assert.Single(info.Connectors).Label);
    }

    [Fact]
    public void The_response_brings_the_tree_and_no_module_list_of_the_tenant()
    {
        InstanceInfo info = Info("Sandbox", Profile(Connector("Avalara", TenantModules.Fiscal, TenantModules.Inventory)),
            origins: ["Dynamics365"], canViewRawJson: true);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(info, Web));
        Assert.Equal(["environment", "automaticIntegration", "inboundScans", "connectors", "canViewRawJson"],
            json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Sandbox", json.RootElement.GetProperty("environment").GetString());
        Assert.True(json.RootElement.GetProperty("inboundScans").GetBoolean());
        Assert.False(json.RootElement.GetProperty("automaticIntegration").GetBoolean());   // poll.enabled não está ligado
        Assert.True(json.RootElement.GetProperty("canViewRawJson").GetBoolean());

        JsonElement connector = Assert.Single(json.RootElement.GetProperty("connectors").EnumerateArray().ToArray());
        Assert.Equal(["adapter", "label", "scheduling", "modules"], connector.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Avalara", connector.GetProperty("adapter").GetString());
        Assert.Equal("Tax Compliance", connector.GetProperty("label").GetString());
        Assert.True(connector.GetProperty("scheduling").GetBoolean());
        Assert.Equal(["Fiscal", "Inventario"], connector.GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_raw_json_permission_is_the_one_the_host_answered(bool canViewRawJson)
    {
        // A Application não decide: ela carrega a resposta que a lista de papéis do servidor deu para o usuário logado.
        InstanceInfo info = Info("Sandbox", Profile(), canViewRawJson: canViewRawJson);

        Assert.Equal(canViewRawJson, info.CanViewRawJson);
    }

    private static InstanceInfo Info(
        string environment, TenantConnectorProfile? profile, IEnumerable<string>? origins = null, bool canViewRawJson = false)
        => InstanceInfo.For(InstanceEnvironment.From(environment), profile, Declarations, origins ?? [], canViewRawJson);

    // O perfil com uma conta e os conectores dados, na ordem em que vêm: a ordem gravada é a que o /info responde.
    private static TenantConnectorProfile Profile(params PlatformConnector[] connectors) => Profile("Avalara", connectors);

    private static TenantConnectorProfile Profile(string platform, params PlatformConnector[] connectors) => new()
    {
        TenantId = "tenant-a",
        InboundAdapter = "Dynamics365",
        Accounts = connectors.Length == 0 ? [] : [new PlatformAccount { Platform = platform, Connectors = connectors }],
    };

    private static PlatformConnector Connector(string adapter, params string[] modules)
        => new() { Adapter = adapter, Modules = [.. modules.Select(m => new ConnectorModule { Module = m })] };

    // As implementações registradas: a Avalara, que se chama Tax Compliance na tela e agenda, e uma segunda plataforma,
    // com outro adapter, que não agenda — o par (plataforma, adapter) é comparado exato.
    private static readonly IAdapterDeclaration[] Declarations =
    [
        new FakeDeclaration("Avalara", "Avalara") { Label = "Tax Compliance", Scheduling = true },
        new FakeDeclaration("Outra", "Outro") { Label = "AvaTax" },
    ];

    private sealed record FakeDeclaration(string Platform, string Adapter) : IAdapterDeclaration
    {
        public string Label { get; init; } = Adapter;

        public bool Scheduling { get; init; }

        public IReadOnlyList<DeclaredModule> Modules { get; init; } = [];
    }
}
