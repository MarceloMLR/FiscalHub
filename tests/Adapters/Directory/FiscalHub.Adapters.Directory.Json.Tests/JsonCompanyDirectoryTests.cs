using FiscalHub.Application.Directory;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Directory.Json.Tests;

/// <summary>Especifica o diretório em JSON: lê empresas e filiais do arquivo e serve pelo modelo padrão.</summary>
public class JsonCompanyDirectoryTests
{
    [Fact]
    public async Task Lists_companies_and_branches_from_json()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, """
            [
              { "code": "111", "name": "Empresa A", "branches": [ { "code": "0001", "name": "Matriz" }, { "code": "0002", "name": "Filial" } ] },
              { "code": "222", "name": "Empresa B", "branches": [ { "code": "0001", "name": "Matriz" } ] }
            ]
            """);

        var directory = new JsonCompanyDirectory(Options.Create(new JsonCompanyDirectoryOptions { FilePath = path }));

        IReadOnlyList<Company> companies = await directory.ListCompaniesAsync();
        Assert.Equal(2, companies.Count);
        Assert.Equal("Empresa A", companies[0].Name);

        IReadOnlyList<Branch> branches = await directory.ListBranchesAsync("111");
        Assert.Equal(2, branches.Count);
        Assert.Equal("Matriz", branches[0].Name);

        Assert.Empty(await directory.ListBranchesAsync("999")); // empresa inexistente

        File.Delete(path);
    }
}
