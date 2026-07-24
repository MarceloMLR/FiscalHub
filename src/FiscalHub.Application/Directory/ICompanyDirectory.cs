namespace FiscalHub.Application.Directory;

/// <summary>
/// Diretório de empresas e filiais — a fonte que alimenta os dropdowns da integração manual. O
/// núcleo pede "as empresas e suas filiais"; como cada adapter busca (API da Avalara em 2 passos,
/// um JSON, outra API) fica escondido atrás desta porta, que devolve sempre o modelo padrão.
/// </summary>
public interface ICompanyDirectory
{
    /// <summary>Lista as empresas disponíveis.</summary>
    Task<IReadOnlyList<Company>> ListCompaniesAsync(CancellationToken ct = default);

    /// <summary>Lista as filiais de uma empresa.</summary>
    Task<IReadOnlyList<Branch>> ListBranchesAsync(string companyCode, CancellationToken ct = default);
}

/// <summary>Empresa no modelo padrão do diretório (código + nome).</summary>
public sealed record Company
{
    public required string Code { get; init; }
    public required string Name { get; init; }
}

/// <summary>Filial no modelo padrão do diretório (código + nome).</summary>
public sealed record Branch
{
    public required string Code { get; init; }
    public required string Name { get; init; }
}
