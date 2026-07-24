namespace FiscalHub.Adapters.Directory.Json;

/// <summary>Configuração do diretório em JSON: caminho do arquivo de empresas/filiais.</summary>
public sealed class JsonCompanyDirectoryOptions
{
    public string FilePath { get; set; } = "companies.json";
}
