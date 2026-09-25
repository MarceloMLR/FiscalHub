namespace FiscalHub.Application.Connectors;

/// <summary>
/// Settings do perfil de conector inválidas (JSON malformado, campo obrigatório ausente, valor fora da
/// faixa). Falha só o tenant dono do perfil — os demais seguem.
/// </summary>
public sealed class ConnectorSettingsException : Exception
{
    public ConnectorSettingsException(string message) : base(message)
    {
    }

    public ConnectorSettingsException(string message, Exception inner) : base(message, inner)
    {
    }
}
