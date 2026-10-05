namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Configuração do adapter Avalara: só a forma da API da plataforma, igual para todos os clientes. A URL e a
/// credencial são do tenant, na seção do ambiente ativo das settings de saída (ADR-0027) — nenhuma opção global as
/// fornece. Mantida pública para permitir o bind no composition root.
/// </summary>
public sealed class AvalaraOptions
{
    /// <summary>Identificador do destino, usado pelo perfil do tenant para selecionar a implementação.</summary>
    public string Destination { get; set; } = "avalara";

    /// <summary>Caminho do endpoint de envio, relativo à URL base do tenant. O valor real é conferido no teste manual.</summary>
    public string DocumentsPath { get; set; } = "documents";

    /// <summary>Caminho do endpoint de token, relativo à URL base, quando a seção não traz <c>tokenUrl</c>.</summary>
    public string TokenPath { get; set; } = "oauth/token";

    /// <summary>
    /// Caminho da listagem de empresas, relativo à URL base (change <c>platform-establishment-resolution</c>, D6). O padrão é
    /// o verificado no sandbox, e o mock atende o mesmo.
    /// </summary>
    public string CompaniesPath { get; set; } = "taxcompliance/v2/empresa";

    /// <summary>Caminho da listagem de contribuintes de uma empresa (<c>?empresaId=</c>), relativo à URL base.</summary>
    public string TaxpayersPath { get; set; } = "taxcompliance/v2/contribuinte";

    /// <summary>
    /// O <c>$top</c> de cada página da listagem, fixo durante ela. Um servidor que limite a página abaixo dele só faz a
    /// leitura dar mais páginas, porque o <c>$skip</c> soma os itens recebidos. Zero ou negativo impede o host de subir.
    /// </summary>
    public int ListingPageSize { get; set; } = 100;

    /// <summary>
    /// O teto de páginas por lista, a guarda geral contra a leitura que não termina. É em páginas, e não em itens: o teto
    /// efetivo em itens é 49 vezes o tamanho real da página (o menor entre o <see cref="ListingPageSize"/> e o limite do
    /// servidor) — 4.900 com páginas de 100, 490 se o servidor limitar a 10. Zero ou negativo impede o host de subir.
    /// </summary>
    public int ListingMaxPages { get; set; } = 50;

    /// <summary>
    /// Margem para renovar o token antes de ele expirar, evitando usar um token que vence no meio
    /// da requisição (e devolve 401).
    /// </summary>
    public TimeSpan TokenRenewalMargin { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Por quanto tempo a recusa de uma credencial pelo endpoint de token é lembrada: nesse intervalo, os envios daquela
    /// credencial falham com o mesmo motivo sem pedir token de novo — N tentativas de login seguidas bloqueiam conta.
    /// Salvar o perfil pela tela esquece a recusa na hora.
    /// </summary>
    public TimeSpan CredentialRefusalHold { get; set; } = TimeSpan.FromMinutes(5);
}
