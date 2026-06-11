namespace FiscalHub.Adapters.Inbound.Xml;

/// <summary>Lançada quando o XML da NF-e não pode ser convertido (campo obrigatório ausente, etc.).</summary>
public sealed class NfeParseException : Exception
{
    public NfeParseException(string message) : base(message)
    {
    }
}
