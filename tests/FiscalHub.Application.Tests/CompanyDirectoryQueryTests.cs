using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o diretório de empresas do tenant logado (spec company-directory, design D2 e D3): a implementação é a do
/// adapter de entrada do perfil, pela comparação exata da origem; o fallback de desenvolvimento só responde quando ela falta,
/// e nunca por um tenant cujo adapter tem implementação. A resposta tem três desfechos: a lista, "sem diretório" com o
/// adapter no texto, e a falha da origem com o motivo seguro.
/// </summary>
public class CompanyDirectoryQueryTests
{
    private static readonly Company Contoso = new() { Code = "44278225000180", Name = "Contoso Entertainment System Brazil" };
    private static readonly Company Mock = new() { Code = "12345678", Name = "Empresa Emitente LTDA" };

    // ---------- a escolha ----------

    [Fact]
    public async Task Tenant_with_dynamics365_gets_its_directory_and_never_the_fallback()
    {
        var d365 = new FakeDirectory("Dynamics365", Contoso);
        var fallback = new FakeDirectory("Local", Mock);

        CompanyDirectoryResult<Company> result = await Query(Profile("Dynamics365"), [d365], fallback).ListCompaniesAsync();

        Assert.Equal(CompanyDirectoryStatus.Listed, result.Status);
        Assert.Equal([Contoso], result.Items);
        Assert.Equal(["tenant-a"], d365.Tenants);   // o tenant do login chega à porta
        Assert.Empty(fallback.Tenants);
    }

    [Fact]
    public async Task Tenant_whose_erp_has_no_directory_gets_the_fallback_when_it_is_registered()
    {
        var fallback = new FakeDirectory("Local", Mock);

        CompanyDirectoryResult<Company> result = await Query(Profile("iScala"), [new FakeDirectory("Dynamics365", Contoso)], fallback)
            .ListCompaniesAsync();

        Assert.Equal(CompanyDirectoryStatus.Listed, result.Status);
        Assert.Equal([Mock], result.Items);
    }

    [Fact]
    public async Task Tenant_whose_erp_has_no_directory_and_no_fallback_is_told_so_naming_the_adapter()
    {
        CompanyDirectoryResult<Company> result = await Query(Profile("iScala"), [new FakeDirectory("Dynamics365", Contoso)], null)
            .ListCompaniesAsync();

        Assert.Equal(CompanyDirectoryStatus.NoDirectory, result.Status);
        Assert.Empty(result.Items);
        Assert.Equal("O ERP deste tenant (iScala) não tem diretório de empresas no hub.", result.Message);
    }

    [Fact]
    public async Task Tenant_without_profile_follows_the_same_rule()
    {
        CompanyDirectoryResult<Company> withFallback = await Query(null, [new FakeDirectory("Dynamics365", Contoso)], new FakeDirectory("Local", Mock))
            .ListCompaniesAsync();
        CompanyDirectoryResult<Company> without = await Query(null, [new FakeDirectory("Dynamics365", Contoso)], null).ListCompaniesAsync();

        Assert.Equal([Mock], withFallback.Items);
        Assert.Equal(CompanyDirectoryStatus.NoDirectory, without.Status);
        Assert.Contains("perfil de conector", without.Message);
    }

    [Fact]
    public async Task Origin_comparison_is_ordinal()
    {
        CompanyDirectoryResult<Company> result = await Query(Profile("dynamics365"), [new FakeDirectory("Dynamics365", Contoso)], null)
            .ListCompaniesAsync();

        Assert.Equal(CompanyDirectoryStatus.NoDirectory, result.Status);
    }

    [Fact]
    public async Task Branches_come_from_the_same_choice_with_the_company_asked()
    {
        var d365 = new FakeDirectory("Dynamics365", Contoso);

        CompanyDirectoryResult<Branch> result = await Query(Profile("Dynamics365"), [d365], null).ListBranchesAsync("44278225000180");

        Assert.Equal(CompanyDirectoryStatus.Listed, result.Status);
        Assert.Equal([new Branch { Code = "Matriz", Name = "Contoso Entertainment System Brazil" }], result.Items);
        Assert.Equal(["44278225000180"], d365.BranchCompanies);
    }

    // ---------- a falha ----------

    [Fact]
    public async Task Origin_failure_is_a_failure_with_its_safe_reason_and_not_an_empty_list()
    {
        var d365 = new FakeDirectory("Dynamics365", Contoso) { Failure = new OriginUnavailableException("O F&O negou a leitura (HTTP 403).") };

        CompanyDirectoryResult<Company> result = await Query(Profile("Dynamics365"), [d365], null).ListCompaniesAsync();

        Assert.Equal(CompanyDirectoryStatus.Unavailable, result.Status);
        Assert.Empty(result.Items);
        Assert.Equal("O F&O negou a leitura (HTTP 403).", result.Message);
    }

    [Fact]
    public async Task Settings_failure_is_a_failure_naming_the_erp_configuration()
    {
        var d365 = new FakeDirectory("Dynamics365", Contoso) { Failure = new ConnectorSettingsException("url do ambiente F&O ausente ou inválida: ''.") };

        CompanyDirectoryResult<Branch> result = await Query(Profile("Dynamics365"), [d365], null).ListBranchesAsync("44278225000180");

        Assert.Equal(CompanyDirectoryStatus.Unavailable, result.Status);
        Assert.Equal("Configuração do ERP: url do ambiente F&O ausente ou inválida: ''.", result.Message);
    }

    // ---------- a regra comum ----------

    [Fact]
    public void Candidates_are_the_adapter_implementation_then_the_fallback()
    {
        var d365 = new FakeDirectory("Dynamics365");
        var fallback = new FakeDirectory("Local");

        Assert.Equal([d365, fallback], InboundAdapterChoice.Candidates([d365], d => d.Origin, "Dynamics365", fallback));
        Assert.Equal([fallback], InboundAdapterChoice.Candidates([d365], d => d.Origin, "iScala", fallback));
        Assert.Equal([d365], InboundAdapterChoice.Candidates([d365], d => d.Origin, "Dynamics365", null));
        Assert.Empty(InboundAdapterChoice.Candidates([d365], d => d.Origin, null, null));
        Assert.Same(d365, InboundAdapterChoice.Pick([d365], d => d.Origin, "Dynamics365", fallback));
    }

    // ---------- apoio ----------

    private static TenantConnectorProfile Profile(string inboundAdapter) => new()
    {
        TenantId = "tenant-a",
        Environment = "Sandbox",
        InboundAdapter = inboundAdapter,
        OutboundAdapter = "Avalara",
    };

    private static CompanyDirectoryQuery Query(TenantConnectorProfile? profile, IEnumerable<ICompanyDirectory> directories, ICompanyDirectory? fallback)
        => new(directories, new Profiles(profile), new Tenant("tenant-a"), fallback);

    private sealed class FakeDirectory(string origin, params Company[] companies) : ICompanyDirectory
    {
        public string Origin => origin;

        public List<string> Tenants { get; } = [];

        public List<string> BranchCompanies { get; } = [];

        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<Company>> ListCompaniesAsync(string tenantId, CancellationToken ct = default)
        {
            Tenants.Add(tenantId);
            return Failure is null ? Task.FromResult<IReadOnlyList<Company>>(companies) : Task.FromException<IReadOnlyList<Company>>(Failure);
        }

        public Task<IReadOnlyList<Branch>> ListBranchesAsync(string tenantId, string companyCode, CancellationToken ct = default)
        {
            Tenants.Add(tenantId);
            BranchCompanies.Add(companyCode);
            return Failure is null
                ? Task.FromResult<IReadOnlyList<Branch>>([.. companies.Where(c => c.Code == companyCode).Select(c => new Branch { Code = "Matriz", Name = c.Name })])
                : Task.FromException<IReadOnlyList<Branch>>(Failure);
        }
    }

    private sealed class Profiles(TenantConnectorProfile? profile) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(profile);

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
