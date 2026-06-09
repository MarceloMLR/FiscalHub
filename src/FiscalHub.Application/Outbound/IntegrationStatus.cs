namespace FiscalHub.Application.Outbound;

/// <summary>Estado da integração de um documento com a plataforma de destino. Eixo exibido no dashboard.</summary>
public enum IntegrationStatus
{
    /// <summary>Ainda não enviado.</summary>
    Pending,

    /// <summary>Enviado e aceito, aguardando confirmação.</summary>
    Submitted,

    /// <summary>Confirmado pela plataforma.</summary>
    Confirmed,

    /// <summary>Rejeitado pela plataforma.</summary>
    IntegrationError,
}
