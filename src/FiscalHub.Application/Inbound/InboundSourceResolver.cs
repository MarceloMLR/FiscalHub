using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Resolve o source pela origem da referência e, sem ela, pelo adapter de entrada do perfil do tenant
/// (fallback que mantém válidas as mensagens publicadas antes do campo existir). A comparação é exata:
/// a origem é o mesmo identificador do <see cref="IDocumentChangeFeed.Origin"/> e do perfil.
/// </summary>
public sealed class InboundSourceResolver<TDocument> : IInboundSourceResolver<TDocument>
{
    private readonly IReadOnlyList<IInboundSource<TDocument>> _sources;
    private readonly IConnectorProfileStore _profiles;

    public InboundSourceResolver(IEnumerable<IInboundSource<TDocument>> sources, IConnectorProfileStore profiles)
    {
        _sources = [.. sources];
        _profiles = profiles;
    }

    public async Task<IInboundSource<TDocument>> ResolveAsync(DocumentReference reference, CancellationToken ct = default)
    {
        string origin = reference.Origin
            ?? (await _profiles.GetAsync(reference.TenantId, ct))?.InboundAdapter
            ?? throw new InboundSourceNotFoundException(
                $"Documento '{reference.NaturalKey}' do tenant '{reference.TenantId}' sem origem na referência e sem perfil de conector no tenant.");

        return _sources.FirstOrDefault(s => string.Equals(s.Origin, origin, StringComparison.Ordinal))
            ?? throw new InboundSourceNotFoundException(
                $"Nenhum adapter de entrada registrado para a origem '{origin}' (tenant '{reference.TenantId}', documento '{reference.NaturalKey}').");
    }
}
