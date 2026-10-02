using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Directory;

/// <summary>O desfecho da leitura do diretório.</summary>
public enum CompanyDirectoryStatus
{
    /// <summary>A lista, que pode ser vazia (uma empresa sem filial no cadastro).</summary>
    Listed,

    /// <summary>O ERP do tenant não tem diretório no hub, ou o tenant não tem perfil.</summary>
    NoDirectory,

    /// <summary>A leitura na origem falhou, ou a configuração do ERP não se lê.</summary>
    Unavailable,
}

/// <summary>A lista, ou o motivo de não haver lista. O motivo vai à tela: nunca leva token, segredo nem cabeçalho.</summary>
public sealed record CompanyDirectoryResult<T>(CompanyDirectoryStatus Status, IReadOnlyList<T> Items, string? Message);

/// <summary>
/// O diretório de empresas e filiais do tenant logado (change erp-company-directory-and-card-filters, D2): a implementação
/// do adapter de entrada do perfil, ou o fallback de desenvolvimento quando ela falta. A falha da origem é dita com o motivo
/// dela, e nunca vira uma lista vazia, que o usuário leria como "não há empresas".
/// </summary>
public sealed class CompanyDirectoryQuery
{
    private readonly IReadOnlyList<ICompanyDirectory> _directories;
    private readonly IConnectorProfileStore _profiles;
    private readonly ITenantContext _tenant;
    private readonly ICompanyDirectory? _developmentFallback;

    public CompanyDirectoryQuery(
        IEnumerable<ICompanyDirectory> directories, IConnectorProfileStore profiles, ITenantContext tenant, ICompanyDirectory? developmentFallback = null)
    {
        _directories = [.. directories];
        _profiles = profiles;
        _tenant = tenant;
        _developmentFallback = developmentFallback;
    }

    public Task<CompanyDirectoryResult<Company>> ListCompaniesAsync(CancellationToken ct = default)
        => ReadAsync((directory, tenantId) => directory.ListCompaniesAsync(tenantId, ct), ct);

    public Task<CompanyDirectoryResult<Branch>> ListBranchesAsync(string companyCode, CancellationToken ct = default)
        => ReadAsync((directory, tenantId) => directory.ListBranchesAsync(tenantId, companyCode, ct), ct);

    private async Task<CompanyDirectoryResult<T>> ReadAsync<T>(
        Func<ICompanyDirectory, string, Task<IReadOnlyList<T>>> read, CancellationToken ct)
    {
        string tenantId = _tenant.TenantId;
        TenantConnectorProfile? profile = await _profiles.GetAsync(tenantId, ct);
        ICompanyDirectory? directory = InboundAdapterChoice.Pick(_directories, d => d.Origin, profile?.InboundAdapter, _developmentFallback);
        if (directory is null)
        {
            string message = profile is null
                ? "Este tenant não tem perfil de conector: sem ERP configurado, não há diretório de empresas."
                : $"O ERP deste tenant ({profile.InboundAdapter}) não tem diretório de empresas no hub.";
            return new(CompanyDirectoryStatus.NoDirectory, [], message);
        }

        try
        {
            return new(CompanyDirectoryStatus.Listed, await read(directory, tenantId), null);
        }
        catch (OriginUnavailableException ex)
        {
            return new(CompanyDirectoryStatus.Unavailable, [], ex.Message);
        }
        catch (ConnectorSettingsException ex)
        {
            return new(CompanyDirectoryStatus.Unavailable, [], $"Configuração do ERP: {ex.Message}");
        }
    }
}
