namespace FiscalHub.Application.Directory;

/// <summary>
/// Diretório de empresas e filiais — a fonte que alimenta os dropdowns da integração manual e do agendamento. O núcleo
/// pede "as empresas e suas filiais" de um tenant; como cada adapter busca (o cadastro do ERP, um JSON, outra API) fica
/// escondido atrás desta porta, que devolve sempre o modelo padrão. A implementação é escolhida pelo adapter de entrada do
/// perfil do tenant (<see cref="Connectors.InboundAdapterChoice"/>), e o tenant é sempre o de quem pediu (ADR-0028).
/// </summary>
public interface ICompanyDirectory
{
    /// <summary>Identificador da origem; casa com o adapter de entrada do perfil do tenant (ex.: "Dynamics365").</summary>
    string Origin { get; }

    /// <summary>Lista as empresas do tenant.</summary>
    Task<IReadOnlyList<Company>> ListCompaniesAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Lista as filiais de uma empresa do tenant. Empresa desconhecida dá a lista vazia.</summary>
    Task<IReadOnlyList<Branch>> ListBranchesAsync(string tenantId, string companyCode, CancellationToken ct = default);
}

/// <summary>Empresa no modelo padrão do diretório (código + nome). O código é a mesma chave dos grupos do dashboard.</summary>
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
