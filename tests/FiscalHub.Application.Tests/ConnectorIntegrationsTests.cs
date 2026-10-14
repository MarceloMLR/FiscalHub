using System.Text.RegularExpressions;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// O vocabulário das integrações (change platform-account-and-connectors, D3): a chave é do hub, e é gravada — por isso
/// estável, sem repetição, e só com minúsculas, dígitos e hífen.
/// </summary>
public partial class ConnectorIntegrationsTests
{
    [Fact]
    public void Keys_are_unique_and_safe_to_store()
    {
        Assert.Equal(ConnectorIntegrations.Known.Count, ConnectorIntegrations.Known.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ConnectorIntegrations.Known, k => Assert.Matches(SafeKey(), k));
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SafeKey();
}
