namespace FiscalHub.Application.Outbound;

/// <summary>
/// Status do PROCESSAMENTO/integração de um documento — o eixo que o dashboard mostra. Não
/// confundir com o status fiscal da nota (emitida/cancelada), que é apenas insumo de roteamento.
///
/// Cada adapter de destino traduz o status nativo da plataforma para este vocabulário comum
/// (camada anticorrupção no status), mantendo o dashboard agnóstico de plataforma.
/// </summary>
public enum IntegrationStatus
{
    /// <summary>Ainda não enviado ao destino.</summary>
    Pending,

    /// <summary>Aceito pela plataforma, aguardando confirmação (caso assíncrono).</summary>
    Submitted,

    /// <summary>Carregado/confirmado com sucesso no destino.</summary>
    Confirmed,

    /// <summary>A plataforma rejeitou a integração.</summary>
    IntegrationError,
}
