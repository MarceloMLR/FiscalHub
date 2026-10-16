namespace FiscalHub.Application.Outbound;

/// <summary>
/// A chave de envio ao conector de saída, do tenant (ADR-0041): ligada, o hub salva e envia; desligada, salva e não
/// envia. Quem a lê é a esteira, para decidir se envia o que preparou — o adapter de saída não a recebe, nem por
/// parâmetro, nem por contexto, nem por injeção, e por isso não tem o que esquecer de olhar.
/// <para>Não confundir com a integração automática (o <c>poll.enabled</c> das settings de entrada, ADR-0029), que é o
/// eixo da <b>captura</b>. Esta é a da <b>saída</b>.</para>
/// </summary>
public interface IOutboundDispatchSwitch
{
    /// <summary>Se o tenant envia ao conector de saída. Tenant sem perfil = ligada, que é o comportamento de sempre.</summary>
    Task<bool> IsOnAsync(string tenantId, CancellationToken ct = default);
}
