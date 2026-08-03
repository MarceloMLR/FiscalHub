namespace FiscalHub.Application.Support;

/// <summary>Anexo de um chamado — já pronto em bytes (no nosso caso, um zip por nota).</summary>
public sealed record TicketAttachment(string FileName, byte[] Content, string ContentType = "application/zip");

/// <summary>Chamado a abrir: assunto, descrição (texto) e anexos. Provider-agnóstico.</summary>
public sealed record SupportTicket
{
    public required string TenantId { get; init; }
    public required string Subject { get; init; }
    public required string DescriptionText { get; init; }
    public IReadOnlyList<TicketAttachment> Attachments { get; init; } = [];
}

/// <summary>Resultado da abertura: id do chamado no provider e a URL pra abrir (quando houver).</summary>
public sealed record TicketResult(string Id, string? Url);

/// <summary>Arquivo de rastreabilidade de uma nota (origem/domínio/destino), cru em bytes.</summary>
public sealed record TraceFile(string Name, byte[] Content);

/// <summary>Erro de regra ao abrir um chamado (config ausente, seleção vazia, anexos grandes demais).</summary>
public sealed class SupportTicketException(string message) : Exception(message);
