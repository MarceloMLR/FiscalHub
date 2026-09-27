using System.IO.Compression;

namespace FiscalHub.Application.Support;

/// <summary>
/// Empacota as fotos de rastreabilidade de uma nota num zip, uma entrada por arquivo, com o nome dele. É o mesmo
/// zip que vai anexado ao chamado de suporte e que o download do documento entrega.
/// </summary>
public static class TraceArchive
{
    public static byte[] Zip(IReadOnlyList<TraceFile> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (TraceFile file in files)
            {
                ZipArchiveEntry entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
                using Stream stream = entry.Open();
                stream.Write(file.Content);
            }
        }

        return ms.ToArray();
    }
}
