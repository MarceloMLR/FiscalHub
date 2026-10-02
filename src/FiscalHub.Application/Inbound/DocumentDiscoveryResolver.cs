using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Escolhe a descoberta por período de um tenant (change erp-company-directory-and-card-filters, D2 e D3): a do adapter de
/// entrada do perfil, pela comparação exata da origem, e o fallback de desenvolvimento só quando ela falta
/// (<see cref="InboundAdapterChoice"/>). A integração manual e a agendada usam uma; o reprocesso pergunta às candidatas, em
/// ordem (D5).
/// </summary>
public sealed class DocumentDiscoveryResolver
{
    private readonly IReadOnlyList<IDocumentDiscovery> _discoveries;
    private readonly IConnectorProfileStore _profiles;
    private readonly IDocumentDiscovery? _developmentFallback;

    public DocumentDiscoveryResolver(
        IEnumerable<IDocumentDiscovery> discoveries, IConnectorProfileStore profiles, IDocumentDiscovery? developmentFallback = null)
    {
        _discoveries = [.. discoveries];
        _profiles = profiles;
        _developmentFallback = developmentFallback;
    }

    /// <summary>As candidatas do tenant, em ordem: a do adapter de entrada, e depois o fallback, quando existe.</summary>
    public async Task<IReadOnlyList<IDocumentDiscovery>> CandidatesAsync(string tenantId, CancellationToken ct = default)
    {
        TenantConnectorProfile? profile = await _profiles.GetAsync(tenantId, ct);
        return InboundAdapterChoice.Candidates(_discoveries, d => d.Origin, profile?.InboundAdapter, _developmentFallback);
    }

    /// <summary>A descoberta que responde pelo tenant. Sem nenhuma, <see cref="DocumentDiscoveryNotFoundException"/>.</summary>
    public async Task<IDocumentDiscovery> ResolveAsync(string tenantId, CancellationToken ct = default)
    {
        TenantConnectorProfile? profile = await _profiles.GetAsync(tenantId, ct);
        return InboundAdapterChoice.Pick(_discoveries, d => d.Origin, profile?.InboundAdapter, _developmentFallback)
            ?? throw new DocumentDiscoveryNotFoundException(profile is null
                ? $"O tenant '{tenantId}' não tem perfil de conector: sem ERP configurado, não há como descobrir notas por período."
                : $"O ERP do tenant '{tenantId}' ({profile.InboundAdapter}) não tem descoberta de notas por período no hub.");
    }
}

/// <summary>
/// Nenhuma descoberta por período atende o adapter de entrada do tenant, ou o tenant não tem perfil. É configuração, e não
/// falha de rede: a execução manual responde com o motivo, e nada é enfileirado.
/// </summary>
public sealed class DocumentDiscoveryNotFoundException : Exception
{
    public DocumentDiscoveryNotFoundException(string message) : base(message)
    {
    }
}
