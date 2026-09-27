using Azure.Identity;
using FiscalHub.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica a montagem do cliente do cofre (ADR-0027). O que muda entre dev e produção é só configuração, e a parte
/// de dev é recusada fora do loopback. A verificação do desafio de autenticação sai da URI, e nunca da configuração:
/// produção não roda com ela desligada.
/// </summary>
public class KeyVaultClientFactoryTests
{
    private const string Production = "https://fiscalhub-conectores.vault.azure.net/";
    private const string Thumbprint = "56BED2BF3C0766AF85BDFCCACC99F67094EE2030";

    [Theory]
    [InlineData("https://localhost:8443/")]
    [InlineData("https://127.0.0.1:8443/")]
    [InlineData("https://[::1]:8443/")]
    public void Challenge_verification_is_off_only_for_a_loopback_vault(string vaultUri)
    {
        var settings = new KeyVaultSecretStoreSettings { VaultUri = vaultUri, Credential = "Emulator", EmulatorCertificateThumbprint = Thumbprint };

        Assert.True(KeyVaultClientFactory.Options(settings).DisableChallengeResourceVerification);
    }

    [Fact]
    public void Challenge_verification_stays_on_for_a_production_vault()
    {
        var settings = new KeyVaultSecretStoreSettings { VaultUri = Production };

        Assert.False(KeyVaultClientFactory.Options(settings).DisableChallengeResourceVerification);
    }

    [Fact]
    public void No_configuration_key_turns_the_verification_off()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretStore:VaultUri"] = Production,
                ["SecretStore:DisableChallengeResourceVerification"] = "true",
                ["SecretStore:Diagnostics:IsLoggingContentEnabled"] = "true",
            })
            .Build();

        KeyVaultSecretStoreSettings settings = config.GetSection("SecretStore").Get<KeyVaultSecretStoreSettings>()!;
        var options = KeyVaultClientFactory.Options(settings);

        Assert.False(options.DisableChallengeResourceVerification);
        Assert.False(options.Diagnostics.IsLoggingContentEnabled);   // o corpo (o valor do segredo) nunca vai para o log
    }

    [Fact]
    public void Emulator_credential_outside_loopback_is_refused_at_startup()
    {
        var settings = new KeyVaultSecretStoreSettings { VaultUri = Production, Credential = "Emulator" };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => KeyVaultClientFactory.Create(settings));
        Assert.Contains("loopback", refused.Message);
    }

    [Fact]
    public void Pinned_certificate_outside_loopback_is_refused_at_startup()
    {
        var settings = new KeyVaultSecretStoreSettings { VaultUri = Production, EmulatorCertificateThumbprint = Thumbprint };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => KeyVaultClientFactory.Create(settings));
        Assert.Contains("loopback", refused.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-uri")]
    [InlineData("http://fiscalhub-conectores.vault.azure.net/")]
    public void Vault_uri_must_be_an_absolute_https_uri(string? vaultUri)
    {
        var settings = new KeyVaultSecretStoreSettings { VaultUri = vaultUri };

        Assert.Throws<InvalidOperationException>(() => KeyVaultClientFactory.Create(settings));
    }

    [Fact]
    public void Unknown_credential_mode_is_refused()
    {
        var settings = new KeyVaultSecretStoreSettings { VaultUri = Production, Credential = "ClientSecret" };

        Assert.Throws<InvalidOperationException>(() => KeyVaultClientFactory.Create(settings));
    }

    [Fact]
    public void Production_uses_the_default_azure_credential()
    {
        var settings = new KeyVaultSecretStoreSettings { VaultUri = Production };

        Assert.IsType<DefaultAzureCredential>(KeyVaultClientFactory.Credential(settings));
    }
}
