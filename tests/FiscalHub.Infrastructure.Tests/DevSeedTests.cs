using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica o seed de dev: usuários, tenants e perfis de conector sempre (sem eles não há login nem credencial); os
/// dados de demonstração — notas, execuções e agendamentos — só com <c>Seed:DemoData</c>. Desligado, limpar a base e
/// subir o host não traz a demonstração de volta.
/// </summary>
public sealed class DevSeedTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly FakeBlobService _blobs = new();
    private readonly ServiceProvider _sp;

    public DevSeedTests()
    {
        _conn.Open();
        _sp = new ServiceCollection()
            .AddDbContext<ProcessingDbContext>(o => o.UseSqlite(_conn))
            .AddSingleton(TimeProvider.System)
            .AddSingleton<BlobServiceClient>(_blobs)
            .BuildServiceProvider();
        using IServiceScope scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProcessingDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Without_demo_data_only_users_tenants_and_connector_profiles_are_seeded()
    {
        await _sp.SeedDevDataAsync(new DevSeedOptions { DemoData = false });

        Counts counts = await CountAsync();
        Assert.True(counts.Users > 0);
        Assert.True(counts.Tenants > 0);
        Assert.True(counts.Profiles > 0);
        Assert.Equal((0, 0, 0), (counts.Documents, counts.Executions, counts.Schedules));
        Assert.Empty(_blobs.Uploaded);   // nem as fotos de demonstração
    }

    [Fact]
    public async Task With_demo_data_the_demonstration_is_seeded_too()
    {
        await _sp.SeedDevDataAsync(new DevSeedOptions { DemoData = true });

        Counts counts = await CountAsync();
        Assert.True(counts.Documents > 0);
        Assert.True(counts.Executions > 0);
        Assert.True(counts.Schedules > 0);
        Assert.NotEmpty(_blobs.Uploaded);
    }

    [Fact]
    public async Task Cleaning_the_demo_tables_and_starting_again_does_not_bring_them_back()
    {
        await _sp.SeedDevDataAsync(new DevSeedOptions { DemoData = true });
        await using (AsyncServiceScope scope = _sp.CreateAsyncScope())
        {
            ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
            await db.ProcessedDocuments.ExecuteDeleteAsync();
            await db.IntegrationExecutions.ExecuteDeleteAsync();
            await db.ScheduledIntegrations.ExecuteDeleteAsync();
        }

        await _sp.SeedDevDataAsync(new DevSeedOptions { DemoData = false });   // o host sobe de novo, em Development

        Counts counts = await CountAsync();
        Assert.Equal((0, 0, 0), (counts.Documents, counts.Executions, counts.Schedules));
        Assert.True(counts.Users > 0 && counts.Tenants > 0 && counts.Profiles > 0);
    }

    [Theory]
    [InlineData(null, true)]      // sem a chave: o comportamento de antes
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void The_key_is_read_from_the_seed_section(string? value, bool expected)
    {
        var values = new Dictionary<string, string?>();
        if (value is not null)
        {
            values["Seed:DemoData"] = value;
        }

        IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Equal(expected, DevSeedOptions.From(cfg).DemoData);
    }

    [Fact]
    public void Development_settings_turn_the_demo_data_off()
    {
        string root = RepoRoot();
        IConfiguration cfg = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "FiscalHub.Host", "appsettings.json"))
            .AddJsonFile(Path.Combine(root, "src", "FiscalHub.Host", "appsettings.Development.json"))
            .Build();

        Assert.False(DevSeedOptions.From(cfg).DemoData);
    }

    public void Dispose()
    {
        _sp.Dispose();
        _conn.Dispose();
    }

    private async Task<Counts> CountAsync()
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        return new Counts(
            await db.Users.CountAsync(), await db.Tenants.CountAsync(), await db.ConnectorProfiles.CountAsync(),
            await db.ProcessedDocuments.CountAsync(), await db.IntegrationExecutions.CountAsync(), await db.ScheduledIntegrations.CountAsync());
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FiscalHub.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    private sealed record Counts(int Users, int Tenants, int Profiles, int Documents, int Executions, int Schedules);

    // O Blob falso, escrito à mão sobre os construtores de teste do SDK: guarda o nome de cada foto gravada.
    private sealed class FakeBlobService : BlobServiceClient
    {
        public List<string> Uploaded { get; } = [];

        public override BlobContainerClient GetBlobContainerClient(string blobContainerName) => new FakeContainer(Uploaded);
    }

    private sealed class FakeContainer(List<string> uploaded) : BlobContainerClient
    {
        // A sobrecarga que o seed chama (CreateIfNotExistsAsync(cancellationToken: ct)).
        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Response<BlobContainerInfo>>(null!);

        public override BlobClient GetBlobClient(string blobName) => new FakeBlob(blobName, uploaded);
    }

    private sealed class FakeBlob(string name, List<string> uploaded) : BlobClient
    {
        public override Task<Response<BlobContentInfo>> UploadAsync(Stream content, BlobUploadOptions options, CancellationToken cancellationToken = default)
        {
            uploaded.Add(name);
            return Task.FromResult<Response<BlobContentInfo>>(null!);
        }
    }
}
