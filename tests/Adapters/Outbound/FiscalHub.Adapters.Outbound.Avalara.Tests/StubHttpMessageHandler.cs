using System.Net;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Fake manual de <see cref="HttpMessageHandler"/> (sem libs de mock): devolve uma resposta
/// pré-configurada e guarda a última requisição/corpo para asserções.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _responseJson;

    public StubHttpMessageHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responseJson = responseJson;
        _status = status;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
