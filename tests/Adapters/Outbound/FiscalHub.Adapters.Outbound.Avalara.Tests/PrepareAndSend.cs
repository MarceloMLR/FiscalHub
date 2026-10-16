using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Preparar e enviar em seguida, que é o que a esteira faz com a chave de envio ligada (ADR-0041). Os testes de
/// desfecho do adapter falam por aqui de propósito: o que eles especificam — o recibo, as recusas, as fotos, o token —
/// não mudou com a divisão do despacho em dois passos, e trocar o nome em cada um deles não provaria nada.
/// <para>Que preparar não envia é o que a <see cref="ComplianceDispatcherContractTests{TDocument}"/> prova; que a
/// esteira pode parar entre os dois é o que os testes do pipeline provam.</para>
/// </summary>
internal static class PrepareAndSend
{
    public static async Task<IntegrationReceipt> SubmitAsync(
        this IComplianceDispatcher<GoodsInvoice> dispatcher, GoodsInvoice document, DispatchContext context, CancellationToken ct = default)
        => await dispatcher.SendAsync(await dispatcher.PrepareAsync(document, context, ct), context, ct);
}
