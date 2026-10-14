using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o teste de credencial (spec connector-credential-test, design D9 e D12): a credencial gravada do tenant
/// logado, o adapter escolhido pelo lado — a entrada, ou a conta de plataforma —, e o freio no endpoint. O freio lembra só
/// a recusa, por 5 minutos, por tenant e por lado — sem ambiente, que é o da instância (change
/// platform-account-and-connectors, D6). O salvar do perfil o esquece. A resposta diz só se funcionou, o motivo e, com o
/// freio, quando um novo teste é aceito.
/// </summary>
public class ConnectorCredentialTestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Tests_the_stored_adapter_of_the_side_with_the_stored_profile()
    {
        var h = new Harness();

        CredentialTestReply reply = await h.Service.TestAsync("inbound");

        Assert.Equal(CredentialTestStatus.Tested, reply.Status);
        Assert.True(reply.Result!.Worked);
        Assert.Equal(["tenant-a"], h.D365.Calls);   // o perfil gravado do tenant do login
        Assert.Empty(h.Avalara.Calls);
    }

    [Fact]
    public async Task Platform_side_tests_the_account_case_insensitive_and_needs_no_environment()
    {
        var h = new Harness();

        CredentialTestReply reply = await h.Service.TestAsync("PLATFORM");

        Assert.Equal(CredentialTestStatus.Tested, reply.Status);
        Assert.Equal(["tenant-a"], h.Avalara.Calls);
        Assert.Equal("Avalara", reply.Log!.Adapter);   // a plataforma da conta gravada
    }

    [Fact]
    public async Task The_old_outbound_side_still_tests_the_account()
    {
        // Uma tela que não recarregou ainda manda "outbound": o lado é o da conta, e não um conector.
        var h = new Harness();

        CredentialTestReply reply = await h.Service.TestAsync("outbound");

        Assert.Equal(CredentialTestStatus.Tested, reply.Status);
        Assert.Equal(["tenant-a"], h.Avalara.Calls);
    }

    [Fact]
    public async Task Tenant_without_a_platform_account_is_unsupported()
    {
        var h = new Harness(platforms: []);

        CredentialTestReply reply = await h.Service.TestAsync("platform");

        Assert.Equal(CredentialTestStatus.Unsupported, reply.Status);
        Assert.Contains("conta de plataforma", reply.Problem);
        Assert.Empty(h.Avalara.Calls);
    }

    [Fact]
    public async Task Platform_without_a_test_is_unsupported()
    {
        var h = new Harness(platforms: ["Sovos"]);

        CredentialTestReply reply = await h.Service.TestAsync("platform");

        Assert.Equal(CredentialTestStatus.Unsupported, reply.Status);
        Assert.Contains("Sovos", reply.Problem);
        Assert.Empty(h.Avalara.Calls);
    }

    [Fact]
    public async Task The_platform_is_found_by_the_exact_name()
    {
        // A conta "avalara" não é a da Avalara: a comparação é exata, como a do conector (D9).
        var h = new Harness(platforms: ["avalara"]);

        CredentialTestReply reply = await h.Service.TestAsync("platform");

        Assert.Equal(CredentialTestStatus.Unsupported, reply.Status);
        Assert.Contains("avalara", reply.Problem);
        Assert.Empty(h.Avalara.Calls);
    }

    [Fact]
    public async Task With_two_accounts_the_one_whose_platform_has_a_test_is_tested()
    {
        var h = new Harness(platforms: ["Sovos", "Avalara"]);

        CredentialTestReply reply = await h.Service.TestAsync("platform");

        Assert.Equal(CredentialTestStatus.Tested, reply.Status);
        Assert.Equal("Avalara", reply.Log!.Adapter);
        Assert.Equal(["tenant-a"], h.Avalara.Calls);
    }

    [Theory]
    [InlineData("support")]
    [InlineData("erp")]
    [InlineData("")]
    public async Task Invalid_side_is_refused_without_testing(string side)
    {
        var h = new Harness();

        CredentialTestReply reply = await h.Service.TestAsync(side);

        Assert.Equal(CredentialTestStatus.Invalid, reply.Status);
        Assert.Empty(h.D365.Calls);
        Assert.Empty(h.Avalara.Calls);
    }

    [Fact]
    public async Task Adapter_without_a_test_is_unsupported()
    {
        var h = new Harness(inboundAdapter: "iScala");

        CredentialTestReply reply = await h.Service.TestAsync("inbound");

        Assert.Equal(CredentialTestStatus.Unsupported, reply.Status);
        Assert.Contains("iScala", reply.Problem);
    }

    [Fact]
    public async Task Tenant_without_profile_is_unsupported()
    {
        var h = new Harness(tenant: "tenant-sem-perfil");

        CredentialTestReply reply = await h.Service.TestAsync("inbound");

        Assert.Equal(CredentialTestStatus.Unsupported, reply.Status);
    }

    [Fact]
    public async Task Refusal_enters_the_brake_and_a_second_test_within_the_hold_answers_it_without_calling_the_adapter()
    {
        var h = new Harness();
        h.D365.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "O Entra ID recusou a credencial (AADSTS7000215).");

        CredentialTestReply first = await h.Service.TestAsync("inbound");
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        CredentialTestReply second = await h.Service.TestAsync("inbound");

        Assert.False(first.Result!.Worked);
        Assert.Equal(Now.AddMinutes(5), first.Result.RetryAt);   // o freio já vale: diz quando um novo teste é aceito
        Assert.False(second.Result!.Worked);
        Assert.Equal("Credenciais ou ambiente inválidos", second.Result.Reason);
        Assert.Equal(Now.AddMinutes(5), second.Result.RetryAt);
        Assert.Contains("AADSTS7000215", second.Log!.Detail);   // o detalhe lembrado vai para o log
        Assert.True(second.Log.FromBrake);
        Assert.Single(h.D365.Calls);   // o segundo clique não foi ao Entra ID
    }

    [Fact]
    public async Task The_platform_brake_has_no_environment()
    {
        // Antes, Sandbox e Produção eram duas chaves do freio; agora a instância tem um ambiente só, e a credencial da conta
        // é uma: o segundo teste dentro do intervalo cai no freio.
        var h = new Harness();
        h.Avalara.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "recusada");

        await h.Service.TestAsync("platform");
        CredentialTestReply second = await h.Service.TestAsync("platform");

        Assert.True(second.Log!.FromBrake);
        Assert.Single(h.Avalara.Calls);
    }

    [Fact]
    public async Task After_the_hold_the_test_calls_the_adapter_again()
    {
        var h = new Harness();
        h.D365.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "recusada");

        await h.Service.TestAsync("inbound");
        h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.Service.TestAsync("inbound");

        Assert.Equal(2, h.D365.Calls.Count);
    }

    [Theory]
    [InlineData(CredentialTestVerdict.Unavailable)]
    [InlineData(CredentialTestVerdict.Incomplete)]
    [InlineData(CredentialTestVerdict.Worked)]
    public async Task Only_the_refusal_enters_the_brake(CredentialTestVerdict verdict)
    {
        var h = new Harness();
        h.D365.Next = new CredentialTestOutcome(verdict, "motivo");

        CredentialTestReply first = await h.Service.TestAsync("inbound");
        await h.Service.TestAsync("inbound");

        Assert.Null(first.Result!.RetryAt);
        Assert.Equal(2, h.D365.Calls.Count);   // indisponível, incompleto e sucesso não travam o botão
    }

    [Fact]
    public async Task Saving_the_profile_forgets_the_brake_of_the_tenant()
    {
        var h = new Harness();
        h.Avalara.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "recusada");
        await h.Service.TestAsync("platform");

        await h.Brake.ProfileSavedAsync("tenant-a");   // o observador do perfil, chamado pelo salvar
        await h.Service.TestAsync("platform");

        Assert.Equal(2, h.Avalara.Calls.Count);
    }

    [Fact]
    public async Task Saving_the_profile_forgets_the_brake_of_both_sides()
    {
        // O salvar esquece o freio de tudo o que o tenant testa (5.8): a entrada e a conta de plataforma.
        var h = new Harness();
        h.D365.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "recusada");
        h.Avalara.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "recusada");
        await h.Service.TestAsync("inbound");
        await h.Service.TestAsync("platform");

        await h.Brake.ProfileSavedAsync("tenant-a");
        await h.Service.TestAsync("inbound");
        await h.Service.TestAsync("platform");

        Assert.Equal(2, h.D365.Calls.Count);
        Assert.Equal(2, h.Avalara.Calls.Count);
    }

    [Fact]
    public async Task Brake_of_one_adapter_or_tenant_does_not_touch_the_others()
    {
        var h = new Harness();
        h.D365.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "recusada");
        await h.Service.TestAsync("inbound");

        await h.Service.TestAsync("platform");                  // o outro lado do mesmo tenant
        await h.ForTenant("tenant-c").TestAsync("inbound");    // outro tenant, mesmo adapter

        Assert.Equal(["tenant-a"], h.Avalara.Calls);
        Assert.Equal(["tenant-a", "tenant-c"], h.D365.Calls);
    }

    [Fact]
    public async Task Saving_another_tenant_does_not_forget_the_brake()
    {
        var h = new Harness();
        h.D365.Next = new CredentialTestOutcome(CredentialTestVerdict.Refused, "recusada");
        await h.Service.TestAsync("inbound");

        await h.Brake.ProfileSavedAsync("tenant-c");
        await h.Service.TestAsync("inbound");

        Assert.Single(h.D365.Calls);
    }

    // ---- As mensagens da tela: curtas, iguais para qualquer adapter, e o detalhe no log (revisão de 2026-10-01) ----

    [Theory]
    [InlineData(CredentialTestVerdict.Worked, true, "Credenciais e conexão válidas")]
    [InlineData(CredentialTestVerdict.Refused, false, "Credenciais ou ambiente inválidos")]
    [InlineData(CredentialTestVerdict.Incomplete, false, "Credenciais ou ambiente inválidos")]
    [InlineData(CredentialTestVerdict.Unavailable, false, "Não foi possível conectar agora. Tente novamente em instantes")]
    public async Task Screen_message_is_short_and_the_detail_goes_to_the_log(CredentialTestVerdict verdict, bool worked, string message)
    {
        var h = new Harness();
        h.Avalara.Next = new CredentialTestOutcome(verdict, "A plataforma recusou a credencial (HTTP 400: client_id invalid).");

        CredentialTestReply reply = await h.Service.TestAsync("platform");

        Assert.Equal(worked, reply.Result!.Worked);
        Assert.Equal(message, reply.Result.Reason);
        Assert.Equal(new CredentialTestLog("Avalara", "Sandbox", verdict, "A plataforma recusou a credencial (HTTP 400: client_id invalid).", false), reply.Log);
    }

    [Fact]
    public async Task The_log_carries_the_environment_of_the_instance_on_both_sides()
    {
        var h = new Harness(environment: "Production");

        CredentialTestReply inbound = await h.Service.TestAsync("inbound");
        CredentialTestReply platform = await h.Service.TestAsync("platform");

        Assert.Equal("Production", inbound.Log!.Environment);
        Assert.Equal("Production", platform.Log!.Environment);
    }

    [Fact]
    public void Result_has_only_worked_reason_and_retry_at()
    {
        var result = new CredentialTestResult(false, "recusada", Now);

        JsonObject json = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();

        Assert.Equal(["reason", "retryAt", "worked"], json.Select(p => p.Key).Order());
    }

    private sealed class Harness
    {
        private readonly Profiles _profiles;
        private readonly IInstanceEnvironment _instance;

        // As contas de plataforma de cada tenant, pela plataforma; sem a lista, só a Avalara.
        public Harness(
            string tenant = "tenant-a", string inboundAdapter = "Dynamics365", string environment = "Sandbox", string[]? platforms = null)
        {
            string[] accounts = platforms ?? ["Avalara"];
            _profiles = new Profiles([Profile("tenant-a", inboundAdapter, accounts), Profile("tenant-c", inboundAdapter, accounts)]);
            _instance = InstanceEnvironment.From(environment);
            Brake = new CredentialTestBrake(new CredentialTestOptions(), Clock);
            Service = ForTenant(tenant);
        }

        public StubClock Clock { get; } = new(Now);

        public FakeTest D365 { get; } = new("Dynamics365", ConnectorSettingsKind.Inbound);

        public FakeTest Avalara { get; } = new("Avalara", ConnectorSettingsKind.Platform);

        public CredentialTestBrake Brake { get; }

        public ConnectorCredentialTestService Service { get; }

        public ConnectorCredentialTestService ForTenant(string tenant)
            => new(_profiles, [D365, Avalara], Brake, _instance, new Tenant(tenant));

        private static TenantConnectorProfile Profile(string tenant, string inboundAdapter, string[] platforms) => new()
        {
            TenantId = tenant,
            InboundAdapter = inboundAdapter,
            Accounts = [.. platforms.Select(p => new PlatformAccount { Platform = p })],
        };
    }

    private sealed class FakeTest(string adapter, ConnectorSettingsKind side) : IConnectorCredentialTest
    {
        public string Adapter => adapter;

        public ConnectorSettingsKind Side => side;

        public CredentialTestOutcome Next { get; set; } = new(CredentialTestVerdict.Worked, "funcionou");

        public List<string> Calls { get; } = [];

        public Task<CredentialTestOutcome> TestAsync(TenantConnectorProfile profile, CancellationToken ct = default)
        {
            Calls.Add(profile.TenantId);
            return Task.FromResult(Next);
        }
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class Profiles(TenantConnectorProfile[] items) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(items.FirstOrDefault(p => p.TenantId == tenantId));

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
