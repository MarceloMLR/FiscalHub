using System.Net.Http.Headers;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Support;
using FiscalHub.Application.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FiscalHub.Host.Tests;

/// <summary>
/// O lugar onde um endpoint com regra de papel se prova (change <c>connector-navigation-and-admin-json</c>, design D8).
/// Sobe um <c>TestServer</c> com a mesma autenticação JWT do host e as mesmas extensões de mapeamento que o
/// <c>Program.cs</c> chama — e nada mais: sem SQL, sem Blob, sem Service Bus e sem HostedService. O que o teste cobre é o
/// registro de verdade: apagar um <c>RequireRole</c> da extensão quebra o teste; parar de chamar a extensão no
/// <c>Program.cs</c> quebra o build, porque as rotas somem.
/// <para>O que ele não cobre é a composição inteira do host (ordem de middleware, DI real), que continua sendo provada
/// pelo ponta a ponta e pela prova manual — com <b>uma</b> exceção, desde a change <c>adapter-key-guard-proof</c>: a
/// chamada da trava da chave do conector tem teste próprio, o <see cref="HostStartupGuardTests"/>, que compõe o host
/// pelo caminho real da subida. É uma linha do <c>Program.cs</c>, e não a composição inteira. São dois harnesses com
/// duas finalidades, e nenhum substitui o outro: este sobe pedaços de propósito, e é rápido porque não precisa de
/// configuração nenhuma; aquele sobe tudo, e paga por isso em chaves de configuração.</para>
/// </summary>
internal sealed class RoleEndpointHarness : IAsyncDisposable
{
    private const string JwtKey = "harness-chave-de-assinatura-com-tamanho-suficiente-para-hs256";

    private readonly WebApplication _app;

    private RoleEndpointHarness(WebApplication app, RecordingNoteTraceReader traces, JwtOptions jwt)
    {
        _app = app;
        Traces = traces;
        _jwt = jwt;
    }

    private readonly JwtOptions _jwt;

    /// <summary>O armazenamento das fotos, de mentira: conta as leituras, para provar que o 403 recusa antes de ler.</summary>
    public RecordingNoteTraceReader Traces { get; }

    /// <param name="rawTraceRoles">Os papéis que veem as fotos cruas; o padrão é o do host.</param>
    /// <param name="files">As fotos que a nota do <paramref name="tenantWithFiles"/> tem.</param>
    /// <param name="profiles">Os perfis gravados, por tenant; sem nenhum, todo tenant responde sem perfil.</param>
    /// <param name="declarations">As declarações de adapter registradas, de onde saem o rótulo e o agendamento.</param>
    public static async Task<RoleEndpointHarness> StartAsync(
        RawTraceRoles? rawTraceRoles = null,
        string tenantWithFiles = "tenant-a",
        IReadOnlyList<TraceFile>? files = null,
        IReadOnlyList<TenantConnectorProfile>? profiles = null,
        IReadOnlyList<IAdapterDeclaration>? declarations = null)
    {
        RawTraceRoles roles = rawTraceRoles ?? RawTraceRoles.Default;
        var traces = new RecordingNoteTraceReader(
            tenantWithFiles,
            files ?? [new TraceFile("dominio.json", """{"chave":"NFe-1"}"""u8.ToArray())]);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        var jwt = new JwtOptions { Key = JwtKey };
        builder.Services.AddSingleton(jwt);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<JwtTokenIssuer>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

        // A mesma autenticação e a mesma política de fallback do host, pela extensão que o Program.cs chama.
        builder.Services.AddFiscalHubJwtAuthentication(jwt);

        // A lista de papéis é um lugar só: o RequireRole das rotas e o /info derivam dela.
        builder.Services.AddSingleton(roles);

        // O que os endpoints precisam, sem infra: as fotos vêm do duplo, o perfil é o do tenant, o ambiente é fixo e não
        // há feed de mudanças registrado. As declarações entram uma a uma, como o host as registra: é delas que o /info
        // tira o rótulo e o agendamento de cada conector.
        builder.Services.AddSingleton<INoteTraceReader>(traces);
        builder.Services.AddScoped<DocumentTraceQuery>();
        builder.Services.AddSingleton<IConnectorProfileStore>(new StubConnectorProfileStore(profiles ?? []));
        builder.Services.AddSingleton<IInstanceEnvironment>(InstanceEnvironment.From(InstanceEnvironment.Sandbox));
        builder.Services.AddSingleton<IEnumerable<IDocumentChangeFeed>>([]);
        foreach (IAdapterDeclaration declaration in declarations ?? [])
        {
            builder.Services.AddSingleton(declaration);
        }

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        // O endpoint trivial do próprio harness: prova que a autenticação está de pé (401 sem token, 200 com).
        app.MapGet("/harness/ping", () => Results.Ok(new { pong = true }));

        app.MapRawTraceEndpoints();
        app.MapInstanceInfoEndpoint();

        await app.StartAsync();
        return new RoleEndpointHarness(app, traces, jwt);
    }

    /// <summary>Um cliente sem token: tudo que não é anônimo responde 401.</summary>
    public HttpClient Anonymous() => _app.GetTestClient();

    /// <summary>Um cliente com o token de um papel e de um tenant, emitido como o host o emite.</summary>
    public HttpClient As(string role, string tenantId = "tenant-a")
    {
        var issuer = new JwtTokenIssuer(_jwt, TimeProvider.System);
        (string token, _) = issuer.Issue(new AppUser
        {
            Id = 1,
            Email = $"{role.ToLowerInvariant()}@fiscosys.com",
            Name = role,
            TenantId = tenantId,
            Role = role,
        });

        HttpClient client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>
/// O armazenamento das fotos, de mentira. Conta as leituras: o 403 do papel sem permissão tem que acontecer **antes** de
/// qualquer leitura — recusar depois de ler seria vazamento por tempo de resposta e por log.
/// </summary>
internal sealed class RecordingNoteTraceReader(string tenantWithFiles, IReadOnlyList<TraceFile> files) : INoteTraceReader
{
    private int _reads;

    /// <summary>Quantas vezes o armazenamento foi lido.</summary>
    public int Reads => Volatile.Read(ref _reads);

    public Task<IReadOnlyList<TraceFile>> ReadAsync(string tenantId, string naturalKey, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _reads);
        IReadOnlyList<TraceFile> found = string.Equals(tenantId, tenantWithFiles, StringComparison.Ordinal) ? files : [];
        return Task.FromResult(found);
    }
}

/// <summary>
/// Os perfis gravados, por tenant, sem banco. O tenant que não está aqui não tem perfil — é o que o teste de papel
/// precisava —, e o <c>/info</c> de cada um responde o perfil dele: é por este duplo que o limite de tenant da árvore de
/// conectores se prova.
/// </summary>
internal sealed class StubConnectorProfileStore(IReadOnlyList<TenantConnectorProfile> profiles) : IConnectorProfileStore
{
    public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(profiles.FirstOrDefault(p => string.Equals(p.TenantId, tenantId, StringComparison.Ordinal)));

    public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
}

/// <summary>
/// Uma declaração de adapter de teste: o rótulo e o agendamento que o <c>/info</c> publica saem daqui, e não do perfil.
/// O padrão é não agendar — só quem declara, agenda.
/// </summary>
internal sealed record FakeAdapterDeclaration(string Platform, string Adapter) : IAdapterDeclaration
{
    public string Label { get; init; } = Adapter;

    public bool Scheduling { get; init; }

    public IReadOnlyList<DeclaredModule> Modules { get; init; } = [];
}
