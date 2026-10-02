using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// O diretório de empresas e filiais do D365 (change erp-company-directory-and-card-filters, D2): o cadastro de
/// estabelecimentos, e não as notas, com a URL, a credencial e as empresas do perfil do tenant, as mesmas do coletor.
/// <list type="bullet">
///   <item><b>Empresa:</b> o CNPJ do estabelecimento, sem a pontuação e com as letras, como texto. É a mesma chave dos
///   grupos do dashboard, sem tradução. O nome é o do estabelecimento de menor código com aquele CNPJ.</item>
///   <item><b>Filial:</b> o código do estabelecimento (<c>FiscalEstablishmentId</c>), com o nome dele.</item>
/// </list>
/// Um estabelecimento sem nota aparece do mesmo jeito: a integração manual precisa listar o que existe no cadastro.
/// </summary>
internal sealed class D365CompanyDirectory : ICompanyDirectory
{
    private readonly IConnectorProfileStore _profiles;
    private readonly D365FiscalEstablishments _establishments;

    public D365CompanyDirectory(
        HttpClient http,
        IConnectorProfileStore profiles,
        ID365TokenProvider tokens,
        D365ChangeFeedOptions options,
        TimeProvider clock,
        ILogger<D365CompanyDirectory> logger)
    {
        _profiles = profiles;
        _establishments = new D365FiscalEstablishments(new D365ODataClient(http, tokens, options, clock), logger);
    }

    public string Origin => D365ChangeFeed.OriginName;

    public async Task<IReadOnlyList<Company>> ListCompaniesAsync(string tenantId, CancellationToken ct = default)
        => [.. (await ReadAsync(tenantId, ct))
            .GroupBy(e => e.Cnpj, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Company { Code = g.Key, Name = g.OrderBy(e => e.Code, StringComparer.Ordinal).First().Name })];

    public async Task<IReadOnlyList<Branch>> ListBranchesAsync(string tenantId, string companyCode, CancellationToken ct = default)
        => [.. (await ReadAsync(tenantId, ct))
            .Where(e => string.Equals(e.Cnpj, companyCode, StringComparison.Ordinal))
            .OrderBy(e => e.Code, StringComparer.Ordinal)
            .DistinctBy(e => e.Code, StringComparer.Ordinal)   // o mesmo código em duas empresas do F&O é uma filial só
            .Select(e => new Branch { Code = e.Code, Name = e.Name })];

    private async Task<IReadOnlyList<D365Establishment>> ReadAsync(string tenantId, CancellationToken ct)
    {
        // Settings primeiro: a configuração inválida falha sem tocar a rede.
        TenantConnectorProfile profile = await _profiles.GetAsync(tenantId, ct)
            ?? throw new ConnectorSettingsException($"Tenant '{tenantId}' sem perfil de conector.");
        return await _establishments.ReadAsync(tenantId, D365InboundSettings.Parse(profile.InboundSettings), ct);
    }
}
