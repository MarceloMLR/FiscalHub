namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// O documento do D365 não pôde ser montado sem escolher um valor em silêncio: imposto órfão, grupo IBS/CBS
/// incompleto, divergência não prevista entre fiscal e contábil… (ADR-0025). Erro permanente: segue o retry nativo
/// até a dead-letter, visível; a foto da fonte já está salva. A mensagem cita voucher, RecIds e campos.
/// </summary>
internal sealed class D365AssemblyException : Exception
{
    public D365AssemblyException(string message) : base(message)
    {
    }
}
