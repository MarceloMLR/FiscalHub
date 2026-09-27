namespace FiscalHub.Application.Outbound;

/// <summary>
/// Lançada por um <see cref="IComplianceDispatcher{TDocument}"/> quando o envio é recusado de forma permanente:
/// a plataforma recusou o conteúdo, ou o conector não consegue montar a requisição (configuração do tenant, dado
/// que o contrato do destino não representa). A esteira grava a rejeição com o <see cref="Reason"/> e conclui a
/// mensagem, sem retentativa — mesmo desfecho da rejeição na validação (ADR-0026). Falha transitória continua
/// como qualquer outra exceção, no retry nativo (ADR-0004).
/// </summary>
public sealed class DispatchRejectedException : Exception
{
    public DispatchRejectedException(string reason) : base(reason) => Reason = reason;

    public DispatchRejectedException(string reason, Exception inner) : base(reason, inner) => Reason = reason;

    /// <summary>
    /// Motivo gravado no registro, já dizendo quem recusou (ex.: "Plataforma de compliance recusou: …",
    /// "Configuração do conector: …", "Contrato do destino: …").
    /// </summary>
    public string Reason { get; }
}
