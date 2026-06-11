namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Configuração do adapter Avalara, ligada via <c>IOptions</c>. Origem real é config/Key Vault —
/// nunca valores commitados. Mantida pública para permitir o bind no composition root.
/// </summary>
public sealed class AvalaraOptions
{
    /// <summary>URL base da plataforma de compliance (ex.: o mock em dev). Sem default — vem da config.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Identificador do destino, usado pelo perfil do tenant para selecionar a implementação.</summary>
    public string Destination { get; set; } = "avalara";

    /// <summary>Caminho relativo do endpoint de envio (fase 1).</summary>
    public string DocumentsPath { get; set; } = "documents";
}
