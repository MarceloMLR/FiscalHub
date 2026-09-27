using System.IO.Compression;
using FiscalHub.Application.Support;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o zip das fotos de uma nota: o mesmo que vai anexado ao chamado e que o download entrega. Cada
/// arquivo vira uma entrada com o próprio nome e o conteúdo intacto.
/// </summary>
public class TraceArchiveTests
{
    [Fact]
    public void Every_file_becomes_an_entry_with_its_name_and_content()
    {
        byte[] zip = TraceArchive.Zip(
        [
            new TraceFile("source.xml", [1, 2, 3]),
            new TraceFile("domain.json", [4, 5]),
            new TraceFile("avalara.json", [6]),
        ]);

        Dictionary<string, byte[]> entries = Read(zip);
        Assert.Equal(["avalara.json", "domain.json", "source.xml"], entries.Keys.Order());
        Assert.Equal([1, 2, 3], entries["source.xml"]);
        Assert.Equal([4, 5], entries["domain.json"]);
        Assert.Equal([6], entries["avalara.json"]);
    }

    [Fact]
    public void No_files_gives_an_empty_archive()
    {
        Assert.Empty(Read(TraceArchive.Zip([])));
    }

    private static Dictionary<string, byte[]> Read(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream stream = entry.Open();
            using var content = new MemoryStream();
            stream.CopyTo(content);
            entries[entry.Name] = content.ToArray();
        }

        return entries;
    }
}
