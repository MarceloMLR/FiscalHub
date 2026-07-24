namespace FiscalHub.Application.Inbound;

/// <summary>
/// Origem do disparo de um documento — define a política de idempotência. <see cref="Event"/>
/// (drop/Event Grid/fila) dedupa: uma nota já enviada não reentra, porque o mesmo evento pode
/// chegar duas vezes e a NF-e autorizada é imutável. <see cref="Manual"/> é uma ação humana
/// explícita ("recarrega este período"): reprocessa mesmo se já confirmada, pois o cliente pode ter
/// corrigido algo na origem e quer reenviar de propósito.
/// </summary>
public enum IngestionTrigger
{
    /// <summary>Gatilho automático por evento (padrão). Idempotente: não reenvia o que já foi enviado.</summary>
    Event,

    /// <summary>Recarga manual explícita. Reprocessa mesmo em estado terminal (Submitted/Confirmed).</summary>
    Manual,
}
