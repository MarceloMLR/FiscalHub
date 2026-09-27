using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>Código da empresa e do contribuinte na plataforma — a tradução do estabelecimento do ERP.</summary>
internal sealed record AvalaraCompanyCodes(string CodigoEmpresa, string CodigoContribuinte);

/// <summary>
/// Settings de saída da Avalara no perfil do tenant, na seção do ambiente ativo (design D4). A tabela
/// <c>establishments</c> traduz o CNPJ do estabelecimento próprio para os códigos da plataforma — requisito permanente,
/// não ajuste de demo: todo cliente tem código diferente entre o ERP e a plataforma. A mesma tabela diz qual parte da
/// nota é a nossa quando a origem não diz (D5). A leitura nunca lança; a falta de configuração vira
/// <see cref="DispatchRejectedException"/> só quando o envio precisa dela.
/// </summary>
internal sealed class AvalaraOutboundSettings
{
    private readonly string _tenantId;
    private readonly string _environment;
    private readonly Dictionary<string, JsonElement>? _establishments;   // chave: CNPJ só com dígitos
    private readonly string? _problem;

    private AvalaraOutboundSettings(string tenantId, string environment, string? baseUrl, Dictionary<string, JsonElement>? establishments, string? problem)
    {
        _tenantId = tenantId;
        _environment = environment;
        BaseUrl = baseUrl;
        _establishments = establishments;
        _problem = problem;
    }

    /// <summary>URL base do ambiente ativo, ou <c>null</c> (o dispatcher cai na config do adapter).</summary>
    public string? BaseUrl { get; }

    public static AvalaraOutboundSettings Read(string tenantId, TenantConnectorProfile? profile)
    {
        if (profile is null)
        {
            return new(tenantId, "?", null, null, $"o tenant '{tenantId}' não tem perfil de conector.");
        }

        string environment = profile.Environment.ToLowerInvariant();   // "sandbox" / "production"
        try
        {
            using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(profile.OutboundSettings) ? "{}" : profile.OutboundSettings);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(environment, out JsonElement section)
                || section.ValueKind != JsonValueKind.Object)
            {
                return new(tenantId, environment, null, null,
                    $"o tenant '{tenantId}' não tem a seção '{environment}' (ambiente ativo) em OutboundSettings.");
            }

            string? baseUrl = section.TryGetProperty("baseUrl", out JsonElement url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
            if (!section.TryGetProperty("establishments", out JsonElement table) || table.ValueKind != JsonValueKind.Object)
            {
                return new(tenantId, environment, baseUrl, null,
                    $"o tenant '{tenantId}', no ambiente '{environment}', não tem OutboundSettings.{environment}.establishments "
                    + "(a tradução do CNPJ do estabelecimento para codigoEmpresa e codigoContribuinte).");
            }

            var establishments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty entry in table.EnumerateObject())
            {
                establishments[Digits(entry.Name)] = entry.Value.Clone();
            }

            return new(tenantId, environment, baseUrl, establishments, null);
        }
        catch (JsonException)
        {
            return new(tenantId, environment, null, null, $"as OutboundSettings do tenant '{tenantId}' não são um JSON válido.");
        }
    }

    /// <summary>
    /// Os códigos da plataforma para o estabelecimento próprio. Nunca vêm do ERP: sem tradução, o envio é rejeitado
    /// nomeando o que falta.
    /// </summary>
    public AvalaraCompanyCodes CodesFor(string establishmentTaxId)
    {
        Dictionary<string, JsonElement> table = Table();
        string cnpj = Digits(establishmentTaxId);
        string where = $"OutboundSettings.{_environment}.establishments";

        if (!table.TryGetValue(cnpj, out JsonElement entry))
        {
            throw Rejected(
                $"o tenant '{_tenantId}', no ambiente '{_environment}', não tem tradução para o estabelecimento {cnpj} "
                + $"(faltam codigoEmpresa e codigoContribuinte em {where}).");
        }

        string? empresa = Text(entry, "codigoEmpresa");
        string? contribuinte = Text(entry, "codigoContribuinte");
        string[] missing = [.. new[] { ("codigoEmpresa", empresa), ("codigoContribuinte", contribuinte) }.Where(f => f.Item2 is null).Select(f => f.Item1)];
        if (missing.Length > 0)
        {
            throw Rejected(
                $"o tenant '{_tenantId}', no ambiente '{_environment}', tem o estabelecimento {cnpj} sem {string.Join(" e ", missing)} "
                + $"em {where}[\"{cnpj}\"].");
        }

        return new AvalaraCompanyCodes(empresa!, contribuinte!);
    }

    /// <summary>
    /// O estabelecimento próprio e o parceiro (a contraparte). A nota diz pela emissão própria ou de terceiros; sem isso,
    /// a única parte que está na tabela é a nossa — nenhuma ou as duas é rejeição citando os dois CNPJs.
    /// </summary>
    public (Party Own, Party Partner) PartiesOf(GoodsInvoice invoice)
    {
        switch (invoice.Issuance)
        {
            case Issuance.Own:
                return (invoice.Issuer, invoice.Recipient);
            case Issuance.ThirdParty:
                return (invoice.Recipient, invoice.Issuer);
        }

        Dictionary<string, JsonElement> table = Table();
        string issuer = Digits(invoice.Issuer.TaxId), recipient = Digits(invoice.Recipient.TaxId);
        bool issuerIsOurs = table.ContainsKey(issuer), recipientIsOurs = table.ContainsKey(recipient);

        return (issuerIsOurs, recipientIsOurs) switch
        {
            (true, false) => (invoice.Issuer, invoice.Recipient),
            (false, true) => (invoice.Recipient, invoice.Issuer),
            (true, true) => throw Rejected(
                $"as duas partes da nota (emitente {issuer}, destinatário {recipient}) estão em OutboundSettings.{_environment}.establishments "
                + "e a nota não diz qual é o estabelecimento próprio."),
            _ => throw Rejected(
                $"nenhuma parte da nota (emitente {issuer}, destinatário {recipient}) está em OutboundSettings.{_environment}.establishments "
                + $"do tenant '{_tenantId}'."),
        };
    }

    private Dictionary<string, JsonElement> Table() => _establishments ?? throw Rejected(_problem!);

    private static DispatchRejectedException Rejected(string what) => new($"Configuração do conector: {what}");

    private static string? Text(JsonElement entry, string name)
        => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out JsonElement v)
           && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static string Digits(string value) => new([.. value.Where(char.IsAsciiDigit)]);
}
