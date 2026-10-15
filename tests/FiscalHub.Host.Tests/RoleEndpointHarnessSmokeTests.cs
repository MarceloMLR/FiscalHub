using System.Net;

namespace FiscalHub.Host.Tests;

/// <summary>
/// O harness de pé (change <c>connector-navigation-and-admin-json</c>, 1.1): a autenticação do host valendo num endpoint
/// trivial do próprio harness. Sem isso, um 401 de um teste de papel não distinguiria "a regra recusou" de "o harness
/// subiu quebrado".
/// </summary>
public class RoleEndpointHarnessSmokeTests
{
    [Fact]
    public async Task Without_a_token_the_harness_answers_401()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.Anonymous().GetAsync("/harness/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task With_a_token_of_any_role_the_harness_answers_200()
    {
        await using RoleEndpointHarness harness = await RoleEndpointHarness.StartAsync();

        HttpResponseMessage response = await harness.As("Viewer").GetAsync("/harness/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
