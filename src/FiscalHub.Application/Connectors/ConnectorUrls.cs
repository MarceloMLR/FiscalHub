using System.Diagnostics.CodeAnalysis;

namespace FiscalHub.Application.Connectors;

/// <summary>
/// A regra de endereço das URLs do perfil — as bases do conector e o endpoint de token da conta
/// (<c>avalara-tenant-authentication</c>): <c>https</c> sempre; <c>http</c> só em loopback, que é o mock local e o teste em
/// memória. A gravação a confere (change <c>platform-account-and-connectors</c>, 5.6), e a leitura do adapter também,
/// porque o seed, a migração e o SQL direto gravam sem passar pela gravação (D4). Uma regra só, nos dois lados.
/// </summary>
public static class ConnectorUrls
{
    /// <summary>A regra, como os motivos a dizem, depois do nome do campo.</summary>
    public const string Rule = "precisa ser uma URL https absoluta (http só em loopback)";

    /// <summary>A URL, se ela segue a regra.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)))
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }
}
