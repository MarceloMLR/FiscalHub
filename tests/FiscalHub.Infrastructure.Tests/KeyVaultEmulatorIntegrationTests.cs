using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Secrets;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Roda o adapter do cofre contra o emulador REAL (Lowkey Vault, do <c>docker-compose.yml</c>), com a mesma montagem
/// de cliente do host de dev. Opt-in: só roda com <c>FISCALHUB_KV_EMULATOR</c> definida (ex.:
/// <c>https://localhost:8443/</c>); sem ela aparece como pulado e não toca a rede — CI inclusive.
/// </summary>
public class KeyVaultEmulatorIntegrationTests
{
    // O certificado padrão do emulador, o mesmo do appsettings.Development.json (conferido na tarefa 5.5).
    private const string EmulatorThumbprint = "56BED2BF3C0766AF85BDFCCACC99F67094EE2030";

    [KeyVaultEmulatorFact]
    public async Task Set_describe_and_get_round_trip_through_the_same_adapter_as_production()
    {
        var settings = new KeyVaultSecretStoreSettings
        {
            VaultUri = Environment.GetEnvironmentVariable("FISCALHUB_KV_EMULATOR"),
            Credential = KeyVaultClientFactory.EmulatorCredential,
            EmulatorCertificateThumbprint = EmulatorThumbprint,
        };
        var store = new KeyVaultSecretStore(KeyVaultClientFactory.Create(settings), TimeProvider.System);
        // Um tenant novo a cada execução: o emulador guarda em memória, mas pode estar no ar há tempo.
        string tenant = $"it{Guid.NewGuid():N}"[..12];
        string name = SecretNames.For(tenant, ConnectorSettingsKind.Outbound, ["sandbox"], "clientSecret");

        Assert.Null(await store.DescribeAsync(name));

        await store.SetAsync(name, "valor-de-integracao");
        SecretDescription? description = await store.DescribeAsync(name);

        Assert.NotNull(description);
        Assert.NotNull(description.UpdatedOn);
        Assert.Equal("valor-de-integracao", await store.GetAsync(name));
    }
}

public sealed class KeyVaultEmulatorFactAttribute : FactAttribute
{
    public KeyVaultEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FISCALHUB_KV_EMULATOR")))
        {
            Skip = "Integração com o emulador do cofre: suba o 'keyvault' do docker compose e defina FISCALHUB_KV_EMULATOR.";
        }
    }
}
