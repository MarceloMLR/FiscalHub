namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>Opções globais da montagem do documento D365 (as por tenant ficam nas settings do perfil). Pública para o bind no composition root.</summary>
public sealed class D365AssemblyOptions
{
    /// <summary>
    /// Expiração absoluta dos cadastros em cache (endereço, cidade). Invalidação só por tempo (design D13): o
    /// endereço é versionado no F&amp;O e um RecId não muda de conteúdo; a cidade quase não muda.
    /// </summary>
    public TimeSpan ReferenceDataTtl { get; set; } = TimeSpan.FromHours(1);
}
