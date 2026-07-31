namespace FiscalHub.Infrastructure.Persistence;

/// <summary>
/// Registro (fino) de um cliente. No modelo por-cliente o tenant é essencialmente config da
/// instância: identidade + dados cadastrais. O que varia de verdade (adapters, segredos) mora no
/// perfil de conector, não aqui.
/// </summary>
internal sealed class TenantRow
{
    public int Id { get; set; }

    /// <summary>Slug estável do tenant (ex.: "tenant-a"). É a chave usada em todo o resto do sistema.</summary>
    public required string TenantId { get; set; }

    public required string Name { get; set; }

    /// <summary>CNPJ do cliente (só dígitos ou formatado; validação fica na borda).</summary>
    public string? Cnpj { get; set; }

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
