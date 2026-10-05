using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// O diretório de empresas e filiais do D365 (change erp-company-directory-and-card-filters, D2): o cadastro de
/// estabelecimentos, e não as notas, com a URL, a credencial e as empresas do perfil do tenant, as mesmas do coletor.
/// <list type="bullet">
///   <item><b>Empresa:</b> uma por raiz do CNPJ (change company-root-in-directory): os estabelecimentos da mesma raiz são
///   filiais da mesma empresa (<see cref="TaxIdentifiers.IsSameCompany"/>). O código é o CNPJ completo do estabelecimento
///   que a representa, e o nome é o dele: o de ordem <c>0001</c>, a matriz; sem ele no cadastro, o de menor ordem presente,
///   com o menor código de desempate. É esse CNPJ que a tela mostra e que um agendamento grava.</item>
///   <item><b>Filial:</b> o código do estabelecimento (<c>FiscalEstablishmentId</c>), com o nome e o CNPJ dele. As filiais
///   de uma empresa são as dos estabelecimentos da mesma raiz: qualquer CNPJ da empresa traz todas, inclusive o de uma
///   filial, gravado por um agendamento antigo.</item>
/// </list>
/// Os grupos do dashboard continuam pelo CNPJ completo do estabelecimento; a ponte entre os dois é a raiz.
/// Um estabelecimento sem nota aparece do mesmo jeito: a integração manual precisa listar o que existe no cadastro.
/// </summary>
internal sealed class D365CompanyDirectory : ICompanyDirectory
{
    private const string MatrizOrder = "0001";

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
            .GroupBy(e => TaxIdentifiers.Root(e.Cnpj), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(Representative)
            .Select(e => new Company { Code = e.Cnpj, Name = e.Name })];

    public async Task<IReadOnlyList<Branch>> ListBranchesAsync(string tenantId, string companyCode, CancellationToken ct = default)
        => [.. (await ReadAsync(tenantId, ct))
            .Where(e => TaxIdentifiers.IsSameCompany(e.Cnpj, companyCode))
            .OrderBy(e => e.Code, StringComparer.Ordinal)
            .DistinctBy(e => e.Code, StringComparer.Ordinal)   // o mesmo código em duas empresas do F&O é uma filial só
            .Select(e => new Branch { Code = e.Code, Name = e.Name, TaxId = e.Cnpj })];

    // O estabelecimento que representa a raiz: a matriz (ordem 0001) e, sem ela no cadastro, o de menor ordem presente.
    // Nunca vazio: o grupo tem ao menos um estabelecimento, e todo estabelecimento lido tem CNPJ.
    private static D365Establishment Representative(IEnumerable<D365Establishment> root)
        => root
            .OrderBy(e => Order(e.Cnpj) == MatrizOrder ? 0 : 1)
            .ThenBy(e => Order(e.Cnpj), StringComparer.Ordinal)
            .ThenBy(e => e.Code, StringComparer.Ordinal)
            .First();

    // A ordem do CNPJ: os 4 caracteres depois da raiz, como texto (no alfanumérico, ela também pode ter letras).
    private static string Order(string cnpj) => cnpj.Length > 8 ? cnpj[8..Math.Min(cnpj.Length, 12)] : string.Empty;

    private async Task<IReadOnlyList<D365Establishment>> ReadAsync(string tenantId, CancellationToken ct)
    {
        // Settings primeiro: a configuração inválida falha sem tocar a rede.
        TenantConnectorProfile profile = await _profiles.GetAsync(tenantId, ct)
            ?? throw new ConnectorSettingsException($"Tenant '{tenantId}' sem perfil de conector.");
        return await _establishments.ReadAsync(tenantId, D365InboundSettings.Parse(profile.InboundSettings), ct);
    }
}
