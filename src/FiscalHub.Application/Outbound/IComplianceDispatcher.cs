namespace FiscalHub.Application.Outbound;

/// <summary>
/// Porta de SAÍDA (destino agnóstico). Despacha um documento de domínio para uma plataforma de
/// compliance em DUAS fases, para suportar integrações assíncronas:
/// <list type="number">
///   <item><see cref="SubmitAsync"/> — envia e recebe um recibo (id externo + status inicial);</item>
///   <item><see cref="CheckStatusAsync"/> — consulta o status final pelo id externo.</item>
/// </list>
/// Plataforma síncrona implementa a fase 2 como trivial; por webhook, o status chega por callback.
/// Cada implementação é uma camada anticorrupção: traduz o nosso modelo limpo para o formato
/// externo, e o status externo de volta para o nosso <see cref="IntegrationStatus"/>.
/// </summary>
public interface IComplianceDispatcher<TDocument>
{
    /// <summary>Identifica o destino (ex.: "Avalara") para o perfil do tenant selecionar.</summary>
    string Destination { get; }

    Task<IntegrationReceipt> SubmitAsync(
        TDocument document, DispatchContext context, CancellationToken ct = default);

    Task<IntegrationResult> CheckStatusAsync(
        string externalId, DispatchContext context, CancellationToken ct = default);
}
