using FiscalHub.Application.Auth;
using FiscalHub.Application.Support;
using FiscalHub.Application.Tracing;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o acesso às fotos de um documento (ADR-0028): só o tenant de quem está logado as vê. Pedir as de
/// outro tenant tem a mesma resposta de um documento sem fotos, e o armazenamento do outro nem é lido.
/// </summary>
public class DocumentTraceQueryTests
{
    [Fact]
    public async Task Other_tenant_is_not_found_and_its_storage_is_never_read()
    {
        var reader = new CountingReader([new TraceFile("source.xml", [1])]);
        var query = new DocumentTraceQuery(reader, new Tenant("tenant-b"));

        IReadOnlyList<TraceFile>? files = await query.GetAsync("tenant-a", "nfe-1");

        Assert.Null(files);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task Own_tenant_receives_the_files()
    {
        var reader = new CountingReader([new TraceFile("source.xml", [1]), new TraceFile("domain.json", [2])]);
        var query = new DocumentTraceQuery(reader, new Tenant("tenant-a"));

        IReadOnlyList<TraceFile>? files = await query.GetAsync("tenant-a", "nfe-1");

        Assert.NotNull(files);
        Assert.Equal(["source.xml", "domain.json"], files.Select(f => f.Name));
        Assert.Equal(("tenant-a", "nfe-1"), reader.LastRequest);
    }

    [Fact]
    public async Task Document_without_photos_is_not_found_like_another_tenant()
    {
        var query = new DocumentTraceQuery(new CountingReader([]), new Tenant("tenant-a"));

        Assert.Null(await query.GetAsync("tenant-a", "nfe-sem-fotos"));
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class CountingReader(IReadOnlyList<TraceFile> files) : INoteTraceReader
    {
        public int Calls { get; private set; }

        public (string TenantId, string NaturalKey)? LastRequest { get; private set; }

        public Task<IReadOnlyList<TraceFile>> ReadAsync(string tenantId, string naturalKey, CancellationToken ct = default)
        {
            Calls++;
            LastRequest = (tenantId, naturalKey);
            return Task.FromResult(files);
        }
    }
}
