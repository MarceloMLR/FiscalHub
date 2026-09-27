using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a referência de segredo (<c>kv:&lt;nome&gt;</c>) e o nome que o servidor deriva para cada campo de segredo
/// (ADR-0027). O nome só usa o que o cofre aceita, começa sempre pelo prefixo exato do tenant, e o separador duplo
/// impede que o prefixo de um tenant seja também o de outro.
/// </summary>
public class SecretNamesTests
{
    [Fact]
    public void Reference_accepts_a_vault_name()
    {
        Assert.True(SecretReference.TryParse("kv:avalara-a-sandbox-secret", out string? name));
        Assert.Equal("avalara-a-sandbox-secret", name);
    }

    [Theory]
    [InlineData("avalara-a-sandbox-secret")]   // sem prefixo
    [InlineData("kv:")]                        // nome vazio
    [InlineData("kv:abc/def")]
    [InlineData("kv:abc+def=")]
    [InlineData("kv:abc_def")]
    [InlineData("kv:abc def")]
    [InlineData(null)]
    public void Reference_refuses_anything_outside_the_vault_name_rule(string? value)
    {
        Assert.False(SecretReference.TryParse(value, out _));
    }

    [Fact]
    public void Reference_refuses_a_name_longer_than_127_characters()
    {
        Assert.True(SecretReference.TryParse("kv:" + new string('a', 127), out _));
        Assert.False(SecretReference.TryParse("kv:" + new string('a', 128), out _));
    }

    [Fact]
    public void Name_is_derived_from_tenant_settings_path_and_field()
    {
        Assert.Equal(
            "fh-tenant-a--outbound--sandbox--clientsecret",
            SecretNames.For("tenant-a", ConnectorSettingsKind.Outbound, ["sandbox"], "clientSecret"));
        Assert.Equal(
            "fh-tenant-a--inbound--auth--clientsecret",
            SecretNames.For("tenant-a", ConnectorSettingsKind.Inbound, ["auth"], "clientSecret"));
        Assert.Equal(
            "fh-tenant-a--support--apikey",
            SecretNames.For("tenant-a", ConnectorSettingsKind.Support, [], "apiKey"));
    }

    [Fact]
    public void Derived_name_is_a_valid_vault_name_in_the_tenant_prefix()
    {
        string name = SecretNames.For("tenant-a", ConnectorSettingsKind.Outbound, ["production"], "clientSecret");

        Assert.True(SecretReference.TryParse(SecretReference.Of(name), out _));
        Assert.True(SecretNames.IsConnectorSecret(name));
        Assert.True(SecretNames.BelongsTo(name, "tenant-a"));
    }

    [Fact]
    public void Tenant_prefix_is_exact_so_one_tenant_never_owns_another_tenants_secret()
    {
        string ofTenantA = SecretNames.For("tenant-a", ConnectorSettingsKind.Outbound, ["sandbox"], "clientSecret");

        // Com hífen simples, "fh-tenant-" seria prefixo de "fh-tenant-a-…" e o tenant "tenant" levaria o segredo.
        Assert.False(SecretNames.BelongsTo(ofTenantA, "tenant"));
        Assert.False(SecretNames.BelongsTo(ofTenantA, "tenant-b"));
    }

    [Theory]
    [InlineData("tenant--a", "sandbox", "clientSecret")]   // o separador dentro de um segmento
    [InlineData("tenant-a", "sand--box", "clientSecret")]
    [InlineData("tenant-a", "sandbox", "client_secret")]
    [InlineData("tenant-a", "sand box", "clientSecret")]
    [InlineData("tenant-a", "", "clientSecret")]
    [InlineData("-tenant-a", "sandbox", "clientSecret")]
    public void Segment_outside_the_rule_is_refused(string tenant, string pathSegment, string field)
    {
        Assert.Throws<ArgumentException>(
            () => SecretNames.For(tenant, ConnectorSettingsKind.Outbound, [pathSegment], field));
    }

    [Fact]
    public void Name_longer_than_the_vault_accepts_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => SecretNames.For("tenant-a", ConnectorSettingsKind.Outbound, [new string('a', 120)], "clientSecret"));
    }

    [Fact]
    public void Only_the_connector_prefix_is_a_connector_secret()
    {
        Assert.True(SecretNames.IsConnectorSecret("fh-tenant-a--outbound--sandbox--clientsecret"));
        Assert.False(SecretNames.IsConnectorSecret("jwt-signing-key"));
        Assert.False(SecretNames.IsConnectorSecret("avalara-a-sandbox-secret"));
    }
}
