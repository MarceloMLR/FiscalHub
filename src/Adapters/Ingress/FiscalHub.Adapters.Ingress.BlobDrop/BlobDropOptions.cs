namespace FiscalHub.Adapters.Ingress.BlobDrop;

/// <summary>Configuração do gatilho de ingestão por drop no Blob (dev local).</summary>
public sealed class BlobDropOptions
{
    /// <summary>Container onde os arquivos "caem" para ingestão.</summary>
    public string DropContainer { get; set; } = "drop";

    /// <summary>Container durável (claim-check) para onde o arquivo é movido antes de enfileirar.</summary>
    public string InboxContainer { get; set; } = "nfe";

    /// <summary>Tenant usado quando o nome do arquivo não traz o prefixo "{tenant}/".</summary>
    public string DefaultTenant { get; set; } = "tenant-a";

    /// <summary>Intervalo da varredura (o Azurite não emite eventos, então é polling).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
