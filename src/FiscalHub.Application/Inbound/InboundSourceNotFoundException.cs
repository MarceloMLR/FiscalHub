namespace FiscalHub.Application.Inbound;

/// <summary>
/// Nenhum adapter de entrada registrado atende a origem do documento — ou não há origem na referência
/// nem perfil no tenant. Erro de configuração permanente; segue o retry nativo até a dead-letter, onde
/// fica visível.
/// </summary>
public sealed class InboundSourceNotFoundException : Exception
{
    public InboundSourceNotFoundException(string message) : base(message)
    {
    }
}
