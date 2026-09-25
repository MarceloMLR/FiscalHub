using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Configuration;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Resolve a referência de segredo <c>kv:&lt;nome&gt;</c> (ADR-0019) pelo <see cref="IConfiguration"/>: no
/// local vem de user-secrets/variável de ambiente; em produção, o provider de configuração do Key Vault
/// põe os segredos no <c>IConfiguration</c> pelo nome — o código é o mesmo.
/// </summary>
internal static class SecretReference
{
    private const string Prefix = "kv:";

    public static bool IsReference(string value) => value.StartsWith(Prefix, StringComparison.Ordinal) && value.Length > Prefix.Length;

    public static string Resolve(string reference, IConfiguration configuration)
    {
        if (!IsReference(reference))
        {
            throw new ConnectorSettingsException("Segredo precisa vir por referência \"kv:<nome>\".");
        }

        string name = reference[Prefix.Length..];
        string? value = configuration[name];
        return string.IsNullOrEmpty(value)
            ? throw new ConnectorSettingsException($"Segredo '{name}' não encontrado na configuração (user-secrets/Key Vault).")
            : value;
    }
}
