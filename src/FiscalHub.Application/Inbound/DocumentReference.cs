using FiscalHub.Application.Metadata;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Inbound;

/// <summary>Referência leve a um documento na origem, usada pela esteira para buscá-lo (claim-check).</summary>
public sealed record DocumentReference
{
    /// <summary>Tenant dono do documento.</summary>
    public required string TenantId { get; init; }

    /// <summary>Tipo do documento.</summary>
    public required DocumentType Type { get; init; }

    /// <summary>Chave de negócio da origem (ex.: chave de acesso da NF-e).</summary>
    public required string NaturalKey { get; init; }

    /// <summary>
    /// Localizador interpretado pelo adapter da origem: caminho no Blob (<c>nfe/nota.xml</c>), chave no ERP
    /// (<c>d365/&lt;empresa&gt;/&lt;FiscalDocumentRecId&gt;</c> no D365), etc.
    /// </summary>
    public required string Locator { get; init; }

    /// <summary>
    /// Gatilho que originou o disparo. Define a política de idempotência: <c>Event</c> (padrão)
    /// dedupa; <c>Manual</c> reprocessa mesmo em estado terminal. Mensagens antigas, sem o campo,
    /// caem no padrão <c>Event</c>.
    /// </summary>
    public IngestionTrigger Trigger { get; init; } = IngestionTrigger.Event;

    /// <summary>
    /// Modo da integração que originou o disparo, para exibição (<c>Manual</c>, <c>ScheduledDaily</c>,
    /// <c>ScheduledOnce</c>). <c>null</c> = entrou sem ação humana (coletor, drop, evento), gravado como
    /// <c>Automatic</c>: esse caminho não passa pelo runner. É só rótulo — a política de idempotência continua no
    /// <see cref="Trigger"/>.
    /// </summary>
    public string? SourceMode { get; init; }

    /// <summary>
    /// Origem que sabe buscar o documento — casa com <see cref="IInboundSource{TDocument}.Origin"/>
    /// (ex.: <c>Dynamics365</c>, <c>Xml</c>). Quem publica preenche: o feed põe a dele, os gatilhos de XML
    /// põem <c>Xml</c>. Mensagens antigas, sem o campo, caem no adapter de entrada do perfil do tenant
    /// (ADR-0025) — o perfil diz o que varrer; a referência diz de onde o documento veio.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// O grupo da nota (empresa, filial, data de referência, número e modelo) como a descoberta o leu na origem, para a
    /// nota que não chega à montagem — ignorada no roteamento, fora do escopo na montagem, ou na dead-letter — entrar no
    /// grupo e nos cards da data fiscal dela. A nota montada é agrupada pela montagem, que prevalece. <c>null</c> = a
    /// origem não o informa na descoberta (XML) ou a mensagem é anterior ao campo.
    /// </summary>
    public DocumentMetadata? Metadata { get; init; }
}
