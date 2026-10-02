namespace FiscalHub.Application.Inbound;

/// <summary>
/// A leitura na origem falhou (o ERP negou, respondeu erro, pediu espera ou recusou a credencial), com um motivo curto que
/// pode ir à tela: sem token, sem segredo e sem cabeçalho com valor. O detalhe fica no log, no adapter, que é quem sabe
/// traduzir a resposta da ponta dele (change erp-company-directory-and-card-filters, D1).
/// </summary>
public sealed class OriginUnavailableException : Exception
{
    public OriginUnavailableException(string reason) : base(reason)
    {
    }

    public OriginUnavailableException(string reason, Exception inner) : base(reason, inner)
    {
    }
}
