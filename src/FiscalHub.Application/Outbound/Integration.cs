namespace FiscalHub.Application.Outbound;

/// <summary>Resultado do envio de um documento: identificador externo e estado inicial.</summary>
public sealed record IntegrationReceipt
{
    /// <summary>Identificador devolvido pela plataforma (ex.: GUID da Avalara).</summary>
    public required string ExternalId { get; init; }

    /// <summary>Estado logo após o envio.</summary>
    public required IntegrationStatus Status { get; init; }

    /// <summary>
    /// O que o documento tinha e o contrato do destino não levou, com o porquê (ex.: "item 1: encargo Other de
    /// 416,25 não enviado (…)"). Vazia quando nada ficou de fora. O registro guarda como observação visível
    /// (ADR-0026).
    /// </summary>
    public IReadOnlyList<string> Omissions { get; init; } = [];
}

/// <summary>
/// Um documento já preparado para o envio: traduzido para o formato do destino e com a foto do payload gravada,
/// parado na linha imediatamente anterior à requisição de envio.
/// <para>O conteúdo é opaco para a Application — cada adapter carrega aqui o que o envio dele precisa. O que o tipo
/// existe para dizer é a ordem: só <see cref="IComplianceDispatcher{TDocument}.PrepareAsync"/> produz um preparado, e
/// <see cref="IComplianceDispatcher{TDocument}.SendAsync"/> só aceita um — então não há como enviar sem ter preparado
/// e fotografado, e o compilador recusa a tentativa (ADR-0040).</para>
/// <para>É classe, e não record, de propósito: sem <c>ToString</c> gerado, o preparado não despeja o payload do
/// documento num log por acidente (ADR-0027).</para>
/// </summary>
public abstract class PreparedDispatch<TDocument>;

/// <summary>Resultado de uma consulta de status.</summary>
public sealed record IntegrationResult
{
    /// <summary>Estado atual da integração.</summary>
    public required IntegrationStatus Status { get; init; }

    /// <summary>Mensagem de erro, quando houver.</summary>
    public string? Message { get; init; }
}
