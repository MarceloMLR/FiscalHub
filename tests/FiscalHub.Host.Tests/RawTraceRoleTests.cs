using System.Net;

namespace FiscalHub.Host.Tests;

/// <summary>
/// A regra de papel das fotos cruas (<c>platform-response-trace</c>: "As fotos cruas só para quem pode ver o JSON";
/// ADR-0039 §5). É teste de **papel**, e não de tenant: o limite de tenant já é provado em <c>tenant-boundary</c>, e o
/// papel nunca foi — um <c>RequireRole</c> apagado por engano passava verde.
/// <para>A nota é do mesmo tenant do usuário e **tem fotos**: o 403 do Viewer não pode ser confundido com o 404 de nota
/// alheia nem com o de nota sem foto. E ele acontece **antes de ler**: o duplo conta as leituras, e a contagem é zero.
/// Recusar depois de ler seria vazamento por tempo de resposta e por log.</para>
/// </summary>
public class RawTraceRoleTests
{
    private const string Key = "NFe-35240112345678000199550010000000011000000011";

    [Theory]
    [InlineData($"/trace/tenant-a/{Key}")]
    [InlineData($"/documents/tenant-a/{Key}/download")]
    public async Task A_viewer_of_the_tenant_gets_403_and_the_storage_is_not_read(string path)
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.As("Viewer", "tenant-a").GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, harness.Traces.Reads);
    }

    [Fact]
    public async Task An_admin_of_the_same_tenant_gets_the_snapshots()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.As("Admin", "tenant-a").GetAsync($"/trace/tenant-a/{Key}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("dominio.json", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, harness.Traces.Reads);
    }

    [Fact]
    public async Task An_admin_of_the_same_tenant_gets_the_zip()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.As("Admin", "tenant-a").GetAsync($"/documents/tenant-a/{Key}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, harness.Traces.Reads);
    }

    [Fact]
    public async Task Without_a_token_the_raw_snapshots_answer_401()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.Anonymous().GetAsync($"/trace/tenant-a/{Key}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, harness.Traces.Reads);
    }

    [Fact]
    public async Task A_role_added_to_the_server_list_gets_the_snapshots()
    {
        // O gancho do papel de Suporte (ADR-0030): entrar na lista do servidor é tudo o que ele precisa — e é a mesma
        // lista que o /info publica para a tela.
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync(new RawTraceRoles(["Admin", "Suporte"]));

        HttpResponseMessage response = await harness.As("Suporte", "tenant-a").GetAsync($"/trace/tenant-a/{Key}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_of_another_tenant_still_gets_the_same_404()
    {
        // O limite de tenant não muda: o documento de outro tenant e o sem fotos têm o mesmo 404 (tenant-boundary).
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.As("Admin", "tenant-b").GetAsync($"/trace/tenant-b/{Key}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("sem fotos para esse documento.", await response.Content.ReadAsStringAsync());
    }
}
