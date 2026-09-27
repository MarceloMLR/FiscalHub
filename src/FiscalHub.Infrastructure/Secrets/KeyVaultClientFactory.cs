using System.Net.Security;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace FiscalHub.Infrastructure.Secrets;

/// <summary>
/// A configuração do cofre (<c>SecretStore</c>, ADR-0027). Entre dev e produção mudam só estes valores, e nenhum código:
/// em dev, a URI de loopback do emulador, a credencial <c>Emulator</c> e a impressão fixada do certificado dele.
/// </summary>
public sealed class KeyVaultSecretStoreSettings
{
    /// <summary>A URI do cofre: <c>https://&lt;cofre&gt;.vault.azure.net/</c> em produção, o loopback do emulador em dev.</summary>
    public string? VaultUri { get; set; }

    /// <summary><c>Default</c> (identidade gerenciada) ou <c>Emulator</c> (credencial fixa, só em loopback).</summary>
    public string Credential { get; set; } = KeyVaultClientFactory.DefaultCredential;

    /// <summary>A impressão SHA-1 do certificado do emulador. O cliente aceita só esse certificado, e só em loopback.</summary>
    public string? EmulatorCertificateThumbprint { get; set; }

    /// <summary>Por quanto tempo o valor lido fica em cache. A gravação pela tela invalida na hora.</summary>
    public int ValueCacheSeconds { get; set; } = 300;
}

/// <summary>
/// Monta o <see cref="SecretClient"/> a partir da configuração, e recusa na subida a configuração de dev fora do
/// loopback — uma configuração de dev nunca vaza para produção. A verificação do desafio de autenticação sai da URI, e
/// nunca da configuração: é desligada só com o cofre em loopback, que é o que o emulador exige.
/// </summary>
internal static class KeyVaultClientFactory
{
    public const string DefaultCredential = "Default";
    public const string EmulatorCredential = "Emulator";

    public static SecretClient Create(KeyVaultSecretStoreSettings settings)
        => new(VaultUri(settings), Credential(settings), Options(settings));

    public static SecretClientOptions Options(KeyVaultSecretStoreSettings settings)
    {
        Uri vault = VaultUri(settings);
        var options = new SecretClientOptions
        {
            // Tirada da URI, sem chave de configuração que a mude: produção nunca roda sem a verificação.
            DisableChallengeResourceVerification = vault.IsLoopback,
        };

        // O corpo do pedido é o valor do segredo: nunca vai para o log do SDK.
        options.Diagnostics.IsLoggingContentEnabled = false;

        if (!string.IsNullOrWhiteSpace(settings.EmulatorCertificateThumbprint))
        {
            RequireLoopback(vault, "a impressão fixada do certificado do emulador");
            string pinned = settings.EmulatorCertificateThumbprint;
            options.Transport = new HttpClientTransport(new HttpClientHandler
            {
                // A validação de TLS nunca é desligada: aceita a cadeia válida, ou exatamente o certificado do emulador.
                ServerCertificateCustomValidationCallback = (_, certificate, _, errors)
                    => errors == SslPolicyErrors.None
                       || string.Equals(certificate?.Thumbprint, pinned, StringComparison.OrdinalIgnoreCase),
            });
        }

        return options;
    }

    public static TokenCredential Credential(KeyVaultSecretStoreSettings settings)
    {
        Uri vault = VaultUri(settings);
        switch (settings.Credential)
        {
            case DefaultCredential:
                return new DefaultAzureCredential();
            case EmulatorCredential:
                RequireLoopback(vault, $"a credencial '{EmulatorCredential}'");
                return new EmulatorTokenCredential();
            default:
                throw new InvalidOperationException(
                    $"SecretStore:Credential '{settings.Credential}' não é conhecida: use '{DefaultCredential}' ou '{EmulatorCredential}'.");
        }
    }

    private static Uri VaultUri(KeyVaultSecretStoreSettings settings)
        => Uri.TryCreate(settings.VaultUri, UriKind.Absolute, out Uri? vault) && vault.Scheme == Uri.UriSchemeHttps
            ? vault
            : throw new InvalidOperationException(
                $"SecretStore:VaultUri precisa ser uma URI https absoluta, e não '{settings.VaultUri}'. O host não sobe sem o cofre.");

    private static void RequireLoopback(Uri vault, string what)
    {
        if (!vault.IsLoopback)
        {
            throw new InvalidOperationException(
                $"SecretStore: {what} só vale com o cofre em loopback (o emulador de dev), e não em '{vault}'.");
        }
    }

    /// <summary>A credencial do emulador: ele só confere se o token existe. Nunca sai do loopback.</summary>
    private sealed class EmulatorTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("emulador-de-dev", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(Token);
    }
}
