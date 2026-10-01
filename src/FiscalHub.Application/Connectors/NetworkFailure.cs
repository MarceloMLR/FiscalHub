using System.Net.Sockets;

namespace FiscalHub.Application.Connectors;

public enum NetworkCause
{
    /// <summary>Nenhuma causa de rede na cadeia: a outra ponta respondeu, e a falha é dela ou da credencial.</summary>
    None,

    /// <summary>O nome do host não existe (DNS). Num endereço configurado, é ambiente inválido, e não indisponibilidade.</summary>
    HostNotFound,

    /// <summary>Rede: conexão recusada ou caída, tempo esgotado.</summary>
    Other,
}

/// <summary>
/// A causa de rede de uma falha, olhando a cadeia inteira de exceções. A recusa de uma credencial, embrulhada pelo
/// Azure.Identity ou pelo <c>HttpClient</c>, não pode ser lida como "fora do ar". Isso levaria o teste de credencial a
/// dizer "tente de novo" a quem tem a credencial errada (prova manual, 2026-10-01).
/// </summary>
public static class NetworkFailure
{
    public static NetworkCause Of(Exception error)
    {
        var cause = NetworkCause.None;
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }
                || e is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData })
            {
                return NetworkCause.HostNotFound;
            }

            // A HttpRequestException com status é uma resposta HTTP, e não falha de rede.
            if (e is HttpRequestException { StatusCode: null } or SocketException or TimeoutException or TaskCanceledException or IOException)
            {
                cause = NetworkCause.Other;
            }
        }

        return cause;
    }
}
