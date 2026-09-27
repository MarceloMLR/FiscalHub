using FiscalHub.Application.Tracing;
using FiscalHub.Infrastructure.Tracing;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica o layout das fotos (ADR-0027): as cinco têm nomes distintos e ficam sob o prefixo do documento — é o que
/// faz o <c>/trace</c> e o zip levarem a resposta da plataforma sem regra nova.
/// </summary>
public class TracePathsTests
{
    [Fact]
    public void The_five_photos_have_distinct_names()
    {
        string[] names =
        [
            TracePaths.Source("json"),
            TracePaths.Domain,
            TracePaths.Outbound("avalara"),
            TracePaths.Response("avalara", TraceExchanges.Submit),
            TracePaths.Response("avalara", TraceExchanges.Status),
        ];

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("avalara.response.submit.json", names[3]);
        Assert.Equal("avalara.response.status.json", names[4]);
    }

    [Fact]
    public void Every_photo_is_under_the_document_prefix()
    {
        string prefix = TracePaths.DocumentPrefix("tenant-a", "202609", "nfe-1");

        foreach (string file in new[] { TracePaths.Domain, TracePaths.Response("avalara", TraceExchanges.Submit), TracePaths.Response("avalara", TraceExchanges.Status) })
        {
            string blob = TracePaths.Blob("tenant-a", "202609", "nfe-1", file);
            Assert.StartsWith(prefix, blob);
            Assert.Equal(file, blob.Split('/')[^1]);   // o leitor devolve o último segmento como nome
        }

        Assert.Equal("tenant-a/202609/nfe-1/", prefix);
    }

    [Fact]
    public void An_exchange_without_a_response_photo_is_refused()
        => Assert.Throws<ArgumentException>(() => TracePaths.Response("avalara", "token"));
}
