using System.Net;
using System.Text;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Fake manual de <see cref="HttpMessageHandler"/> (sem libs de mock), no estilo do stub do Avalara, mas
/// com uma FILA de respostas: cada requisição consome a próxima. Guarda todas as requisições (URL e
/// headers) para asserções. Acabou a fila = falha do teste.
/// </summary>
internal sealed class SequencedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Enfileira uma resposta JSON, com status, header <c>Date</c> e demais headers opcionais.</summary>
    public SequencedHttpMessageHandler Respond(
        string json, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage>? headers = null, DateTimeOffset? date = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            response.Headers.Date = date;
            headers?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"Requisição inesperada (fila vazia): {request.RequestUri}");
        }

        return Task.FromResult(_responses.Dequeue()());
    }
}
