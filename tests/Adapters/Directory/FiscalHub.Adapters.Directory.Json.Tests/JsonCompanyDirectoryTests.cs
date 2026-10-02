using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Directory.Json.Tests;

/// <summary>
/// Especifica o diretório em JSON: lê empresas e filiais do arquivo e serve pelo modelo padrão, como o fallback de
/// desenvolvimento (change erp-company-directory-and-card-filters, D3), registrado com a chave do fallback e nunca como
/// implementação comum.
/// </summary>
public class JsonCompanyDirectoryTests
{
    private const string Json = """
        [
          { "code": "111", "name": "Empresa A", "branches": [ { "code": "0001", "name": "Matriz" }, { "code": "0002", "name": "Filial" } ] },
          { "code": "222", "name": "Empresa B", "branches": [ { "code": "0001", "name": "Matriz" } ] }
        ]
        """;

    [Fact]
    public async Task Lists_companies_and_branches_from_json()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, Json);

        var directory = new JsonCompanyDirectory(Options.Create(new JsonCompanyDirectoryOptions { FilePath = path }));

        IReadOnlyList<Company> companies = await directory.ListCompaniesAsync("tenant-a");
        Assert.Equal(2, companies.Count);
        Assert.Equal("Empresa A", companies[0].Name);

        IReadOnlyList<Branch> branches = await directory.ListBranchesAsync("tenant-a", "111");
        Assert.Equal(2, branches.Count);
        Assert.Equal("Matriz", branches[0].Name);

        Assert.Empty(await directory.ListBranchesAsync("tenant-a", "999")); // empresa inexistente

        File.Delete(path);
    }

    [Fact]
    public async Task Is_development_data_the_same_list_for_every_tenant()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, Json);
        var directory = new JsonCompanyDirectory(Options.Create(new JsonCompanyDirectoryOptions { FilePath = path }));

        Assert.Equal(await directory.ListCompaniesAsync("tenant-a"), await directory.ListCompaniesAsync("tenant-b"));
        Assert.Equal("Local", directory.Origin);

        File.Delete(path);
    }

    [Fact]
    public async Task Registers_only_as_the_development_fallback()
    {
        var services = new ServiceCollection();
        services.UseJsonCompanyDirectoryAsDevelopmentFallback(o => o.FilePath = "companies.json");
        await using ServiceProvider sp = services.BuildServiceProvider();

        Assert.Empty(sp.GetServices<ICompanyDirectory>());   // não é implementação comum: não entra na escolha pelo adapter
        Assert.IsType<JsonCompanyDirectory>(sp.GetRequiredKeyedService<ICompanyDirectory>(InboundAdapterChoice.DevelopmentFallbackKey));
    }
}
