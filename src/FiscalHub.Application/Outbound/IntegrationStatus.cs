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

    /// <summary>
    /// Fora do escopo desta esteira (ex.: NFS-e, CT-e, nota não autorizada) — desfecho explícito, com o motivo
    /// no registro (ADR-0025). Não é falha: não entra nos KPIs de falha do dashboard.
    /// </summary>
    Ignored,

    /// <summary>
    /// Traduzida e com o payload de destino gravado, mas <b>não enviada</b>: a chave de envio ao conector de saída do
    /// tenant está desligada (ADR-0040). Não é "Finalizado" — plataforma nenhuma a confirmou — nem "Em processamento",
    /// que quer dizer aguardando retorno do compliance, e uma nota que nunca foi enviada não aguarda nada. Não é falha,
    /// como a ignorada. Sai de "Salva" pelo reprocesso manual, e só por ele.
    /// </summary>
    Saved,
}
