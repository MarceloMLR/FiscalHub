namespace FiscalHub.Application.Outbound;

/// <summary>
/// Envia um documento a uma plataforma de compliance e consulta o resultado. Envio e consulta são
/// separados para suportar plataformas assíncronas; cada implementação traduz documento e status
/// para o formato da plataforma.
/// </summary>
public interface IComplianceDispatcher<TDocument>
{
    /// <summary>Identificador do destino, usado pelo perfil do tenant para selecionar a implementação.</summary>
    string Destination { get; }

    /// <summary>Envia o documento e retorna o recibo com o identificador externo.</summary>
    Task<IntegrationReceipt> SubmitAsync(TDocument document, DispatchContext context, CancellationToken ct = default);

    /// <summary>Consulta o status atual da integração pelo identificador externo.</summary>
    Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default);
}
