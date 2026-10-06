namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica o que os testes contra o F&amp;O real pedem do ambiente (change explicit-credential-and-execution-cnpj, D8): a
/// URL e a credencial do app do conector, por variável de ambiente, e o motivo do skip nomeando exatamente o que falta.
/// Sem rede e sem variável de verdade: a leitura é uma função.
/// </summary>
public class D365IntegrationEnvironmentTests
{
    private const string Url = "FISCALHUB_D365_URL";
    private const string EntraTenant = "FISCALHUB_D365_ENTRA_TENANT_ID";
    private const string ClientId = "FISCALHUB_D365_CLIENT_ID";
    private const string Secret = "FISCALHUB_D365_CLIENT_SECRET";

    [Fact]
    public void Without_any_variable_all_four_are_missing_in_order()
    {
        Assert.Equal([Url, EntraTenant, ClientId, Secret], D365IntegrationEnvironment.Missing(Read()));
    }

    [Fact]
    public void With_only_the_url_the_three_credential_variables_are_missing()
    {
        Assert.Equal([EntraTenant, ClientId, Secret], D365IntegrationEnvironment.Missing(Read((Url, "https://fiscosysdev.operations.dynamics.com"))));
    }

    [Fact]
    public void The_skip_reason_names_only_what_is_missing()
    {
        string? reason = D365IntegrationEnvironment.SkipReason(Read(
            (Url, "https://fiscosysdev.operations.dynamics.com"), (EntraTenant, "entra-a"), (ClientId, "app-a")));

        Assert.Equal(
            "Integração com F&O real: falta FISCALHUB_D365_CLIENT_SECRET. Defina FISCALHUB_D365_URL, "
            + "FISCALHUB_D365_ENTRA_TENANT_ID, FISCALHUB_D365_CLIENT_ID e FISCALHUB_D365_CLIENT_SECRET (o app do conector) para rodar.",
            reason);
    }

    [Fact]
    public void The_skip_reason_lists_several_missing_variables()
    {
        string? reason = D365IntegrationEnvironment.SkipReason(Read((Url, "https://fiscosysdev.operations.dynamics.com")));

        Assert.StartsWith(
            "Integração com F&O real: falta FISCALHUB_D365_ENTRA_TENANT_ID, FISCALHUB_D365_CLIENT_ID e FISCALHUB_D365_CLIENT_SECRET. ",
            reason);
    }

    [Fact]
    public void A_blank_value_counts_as_missing()
    {
        Assert.Equal([Secret], D365IntegrationEnvironment.Missing(Read(
            (Url, "https://fiscosysdev.operations.dynamics.com"), (EntraTenant, "entra-a"), (ClientId, "app-a"), (Secret, "  "))));
    }

    [Fact]
    public void With_all_four_nothing_is_missing_and_there_is_no_skip()
    {
        Func<string, string?> read = Read(
            (Url, "https://fiscosysdev.operations.dynamics.com"), (EntraTenant, "entra-a"), (ClientId, "app-a"), (Secret, "s3cr3t"));

        Assert.Empty(D365IntegrationEnvironment.Missing(read));
        Assert.Null(D365IntegrationEnvironment.SkipReason(read));
    }

    private static Func<string, string?> Read(params (string Name, string Value)[] values)
    {
        Dictionary<string, string> map = values.ToDictionary(v => v.Name, v => v.Value);
        return name => map.TryGetValue(name, out string? value) ? value : null;
    }
}
