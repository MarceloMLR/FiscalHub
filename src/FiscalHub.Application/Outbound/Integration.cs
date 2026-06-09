namespace FiscalHub.Application.Outbound;

/// <summary>Resultado do envio de um documento: identificador externo e estado inicial.</summary>
public sealed record IntegrationReceipt
{
    /// <summary>Identificador devolvido pela plataforma (ex.: GUID da Avalara).</summary>
    public required string ExternalId { get; init; }

    /// <summary>Estado logo após o envio.</summary>
    public required IntegrationStatus Status { get; init; }
}

/// <summary>Resultado de uma consulta de status.</summary>
public sealed record IntegrationResult
{
    /// <summary>Estado atual da integração.</summary>
    public required IntegrationStatus Status { get; init; }

    /// <summary>Mensagem de erro, quando houver.</summary>
    public string? Message { get; init; }
}
