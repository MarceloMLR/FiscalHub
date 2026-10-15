using System.Net;
using System.Text.Json;

namespace FiscalHub.Host.Tests;

/// <summary>
/// O <c>/info</c> diz, para o usuário logado, se ele pode ver as fotos cruas (<c>platform-response-trace</c>; design D7).
/// É um booleano, e não a lista: o servidor já sabe quem está logado, e responde a pergunta em vez de mandar os dados
/// para a tela decidir de novo.
/// <para>E é derivado da **mesma** lista do <c>RequireRole</c>: o teste com dois papéis prova que um papel acrescentado
/// no servidor chega à resposta sem mais nada — nem no servidor, nem no dashboard.</para>
/// </summary>
public class InfoRawJsonTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Viewer", false)]
    public async Task The_info_says_whether_the_logged_user_sees_the_raw_snapshots(string role, bool expected)
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        Assert.Equal(expected, await CanViewRawJsonAsync(harness, role));
    }

    [Fact]
    public async Task A_role_added_to_the_server_list_reaches_the_response()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync(new RawTraceRoles(["Admin", "Suporte"]));

        Assert.True(await CanViewRawJsonAsync(harness, "Suporte"));
        Assert.True(await CanViewRawJsonAsync(harness, "Admin"));
        Assert.False(await CanViewRawJsonAsync(harness, "Viewer"));
    }

    [Fact]
    public async Task The_info_answers_any_role_of_the_tenant()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.As("Viewer").GetAsync("/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_the_info_answers_401()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.Anonymous().GetAsync("/info");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<bool> CanViewRawJsonAsync(RoleEndpointHarness harness, string role)
    {
        HttpResponseMessage response = await harness.As(role).GetAsync("/info");
        response.EnsureSuccessStatusCode();

        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("canViewRawJson").GetBoolean();
    }
}
