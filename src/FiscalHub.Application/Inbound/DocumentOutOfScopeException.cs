namespace FiscalHub.Application.Inbound;

/// <summary>
/// Lançada por um <see cref="IInboundSource{TDocument}"/> quando, ao buscar, constata que o documento saiu do
/// escopo desde a descoberta (ex.: modelo diferente de 55, nota não autorizada). Não é falha: o roteador grava
/// o desfecho "ignorado" com o <see cref="Reason"/> e conclui a mensagem, sem retentativa (ADR-0025).
/// </summary>
public sealed class DocumentOutOfScopeException : Exception
{
    public DocumentOutOfScopeException(string reason) : base(reason) => Reason = reason;

    /// <summary>Motivo gravado no registro do documento (ex.: "ignorado: status Cancelled fora do escopo").</summary>
    public string Reason { get; }
}
