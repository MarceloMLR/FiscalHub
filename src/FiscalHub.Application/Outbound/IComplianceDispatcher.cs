namespace FiscalHub.Application.Outbound;

/// <summary>
/// Envia um documento a uma plataforma de compliance e consulta o resultado. Envio e consulta são
/// separados para suportar plataformas assíncronas; cada implementação traduz documento e status
/// para o formato da plataforma.
/// <para>O envio, por sua vez, é dois passos: preparar e enviar (ADR-0040). Quem decide se o documento vai à
/// plataforma é a esteira, que lê a chave de envio do tenant — o adapter não recebe a chave, por parâmetro, por
/// contexto ou por injeção, e por isso não tem o que esquecer de olhar.</para>
/// </summary>
public interface IComplianceDispatcher<TDocument>
{
    /// <summary>Identificador do destino, usado pelo perfil do tenant para selecionar a implementação.</summary>
    string Destination { get; }

    /// <summary>
    /// Faz tudo o que antecede o envio: lê a configuração do tenant, traduz o documento para o formato do destino e
    /// grava a foto do payload. Devolve o preparado que o <see cref="SendAsync"/> exige.
    /// <para><b>Não faz a requisição de envio.</b> Requisição de leitura que a tradução precise (listar cadastros da
    /// plataforma, por exemplo) é permitida — ler não é enviar. Postar o documento daqui é violação deste contrato, e
    /// a suíte de conformidade do adapter existe para pegá-la.</para>
    /// <para>As recusas permanentes que se descobrem antes do envio (configuração faltando, contrato do destino que não
    /// representa o documento) saem daqui, como <see cref="DispatchRejectedException"/>.</para>
    /// </summary>
    Task<PreparedDispatch<TDocument>> PrepareAsync(TDocument document, DispatchContext context, CancellationToken ct = default);

    /// <summary>
    /// Faz a requisição de envio do que foi preparado e retorna o recibo com o identificador externo. É o único método
    /// que envia.
    /// </summary>
    Task<IntegrationReceipt> SendAsync(PreparedDispatch<TDocument> prepared, DispatchContext context, CancellationToken ct = default);

    /// <summary>Consulta o status atual da integração pelo identificador externo.</summary>
    Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default);
}
