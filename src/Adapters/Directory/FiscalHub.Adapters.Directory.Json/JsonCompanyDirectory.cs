using System.Text.Json;
using FiscalHub.Application.Directory;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Directory.Json;

/// <summary>
/// Diretório de dev local lido de um arquivo JSON, com as empresas fictícias dos XMLs de exemplo. É o fallback de
/// desenvolvimento do diretório (change erp-company-directory-and-card-filters, D3): só existe em Development, e só responde
/// pelo tenant cujo ERP não tem diretório próprio. O tenant chega e é ignorado de propósito: é dado de desenvolvimento, a
/// mesma lista para todos. Carrega o arquivo uma vez (cache).
/// </summary>
internal sealed class JsonCompanyDirectory : ICompanyDirectory
{
    /// <summary>A origem de desenvolvimento, a mesma do catálogo local de descoberta, que espelha os mesmos XMLs.</summary>
    public const string OriginName = "Local";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly Lazy<IReadOnlyList<CompanyEntry>> _companies;

    public JsonCompanyDirectory(IOptions<JsonCompanyDirectoryOptions> options)
    {
        string path = options.Value.FilePath;
        _companies = new Lazy<IReadOnlyList<CompanyEntry>>(() =>
            JsonSerializer.Deserialize<List<CompanyEntry>>(File.ReadAllText(path), JsonOpts) ?? []);
    }

    public string Origin => OriginName;

    public Task<IReadOnlyList<Company>> ListCompaniesAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Company>>(
            _companies.Value.Select(c => new Company { Code = c.Code, Name = c.Name }).ToList());

    public Task<IReadOnlyList<Branch>> ListBranchesAsync(string tenantId, string companyCode, CancellationToken ct = default)
    {
        CompanyEntry? company = _companies.Value.FirstOrDefault(c => c.Code == companyCode);
        IReadOnlyList<Branch> branches = company is null
            ? []
            : company.Branches.Select(b => new Branch { Code = b.Code, Name = b.Name }).ToList();
        return Task.FromResult(branches);
    }

    private sealed record CompanyEntry
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public List<BranchEntry> Branches { get; init; } = [];
    }

    private sealed record BranchEntry
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }
}
