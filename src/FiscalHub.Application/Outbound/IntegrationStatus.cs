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

    /// <summary>Consultado além do limite sem resposta da plataforma (ex.: 204 eterno). Item em aberto.</summary>
    Unconfirmed,

    /// <summary>Processamento falhou repetidamente e a mensagem foi pra dead-letter. Item em aberto.</summary>
    DeadLettered,
}
