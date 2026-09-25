using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Settings do adapter <c>Dynamics365</c> nas <c>InboundSettings</c> do perfil do tenant (ADR-0019). A
/// seção <c>poll</c> é do worker e não é lida aqui. Nenhum valor de cliente em código: URL, empresas,
/// página, mapa de modelos e credenciais são dado por tenant. Segredo só por referência (<c>kv:</c>).
/// </summary>
internal sealed record D365InboundSettings
{
    public const int DefaultPageSize = 500;

    /// <summary>Teto de página do servidor F&amp;O; acima dele uma página truncada seria confundida com a última.</summary>
    public const int MaxPageSize = 10_000;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Mapa padrão modelo → tipo de documento (códigos nacionais, não de cliente).</summary>
    public static readonly IReadOnlyDictionary<string, DocumentType> DefaultModelTypes =
        new Dictionary<string, DocumentType>(StringComparer.OrdinalIgnoreCase)
        {
            ["55"] = DocumentType.GoodsInvoice55,
            ["57"] = DocumentType.Transport57,
            ["SE"] = DocumentType.ServiceNfse,
        };

    /// <summary>URL do ambiente F&amp;O (ex.: https://fiscosysdev.operations.dynamics.com).</summary>
    public required Uri Url { get; init; }

    /// <summary>Empresas (<c>dataAreaId</c>) a consultar. Vazio = todas que o usuário de integração enxerga.</summary>
    public IReadOnlyList<string> Companies { get; init; } = [];

    public int PageSize { get; init; } = DefaultPageSize;

    public IReadOnlyDictionary<string, DocumentType> ModelTypes { get; init; } = DefaultModelTypes;

    /// <summary>Credenciais do client credentials. Opcional quando o host usa o token do Azure CLI (dev).</summary>
    public D365AuthSettings? Auth { get; init; }

    /// <summary>Lê e valida o JSON de settings. Inválido → <see cref="ConnectorSettingsException"/>.</summary>
    public static D365InboundSettings Parse(string? json)
    {
        SettingsDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<SettingsDto>(string.IsNullOrWhiteSpace(json) ? "{}" : json, JsonOpts)
                ?? new SettingsDto();
        }
        catch (JsonException ex)
        {
            throw new ConnectorSettingsException("Settings do adapter Dynamics365 não são um JSON válido.", ex);
        }

        if (!Uri.TryCreate(dto.Url, UriKind.Absolute, out Uri? url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            throw new ConnectorSettingsException($"url do ambiente F&O ausente ou inválida: '{dto.Url}'. Informe uma URL absoluta.");
        }

        int pageSize = dto.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new ConnectorSettingsException($"pageSize deve estar entre 1 e {MaxPageSize} (veio {pageSize}).");
        }

        List<string> companies = dto.Companies ?? [];
        if (companies.Any(string.IsNullOrWhiteSpace))
        {
            throw new ConnectorSettingsException("companies não pode ter empresa vazia.");
        }

        return new D365InboundSettings
        {
            Url = url,
            Companies = companies,
            PageSize = pageSize,
            ModelTypes = ParseModelTypes(dto.ModelTypes),
            Auth = ParseAuth(dto.Auth),
        };
    }

    private static IReadOnlyDictionary<string, DocumentType> ParseModelTypes(Dictionary<string, string>? raw)
    {
        if (raw is null)
        {
            return DefaultModelTypes;
        }

        var map = new Dictionary<string, DocumentType>(StringComparer.OrdinalIgnoreCase);
        foreach ((string model, string type) in raw)
        {
            if (!Enum.TryParse(type, ignoreCase: true, out DocumentType parsed) || !Enum.IsDefined(parsed))
            {
                throw new ConnectorSettingsException($"modelTypes: tipo '{type}' do modelo '{model}' não existe.");
            }

            map[model] = parsed;
        }

        return map;
    }

    private static D365AuthSettings? ParseAuth(AuthDto? raw)
    {
        if (raw is null)
        {
            return null;
        }

        // Segredo em claro nunca: nem num campo "clientSecret", nem numa referência sem o prefixo kv:.
        if (raw.ClientSecret is not null)
        {
            throw new ConnectorSettingsException("auth.clientSecret em claro não é aceito: use auth.clientSecretRef = \"kv:<nome>\".");
        }

        if (raw.ClientSecretRef is not null && !SecretReference.IsReference(raw.ClientSecretRef))
        {
            throw new ConnectorSettingsException("auth.clientSecretRef deve ser uma referência \"kv:<nome>\", nunca o segredo em claro.");
        }

        return new D365AuthSettings(raw.TenantId, raw.ClientId, raw.ClientSecretRef);
    }

    private sealed record SettingsDto
    {
        public string? Url { get; init; }
        public List<string>? Companies { get; init; }
        public int? PageSize { get; init; }
        public Dictionary<string, string>? ModelTypes { get; init; }
        public AuthDto? Auth { get; init; }
    }

    private sealed record AuthDto
    {
        public string? TenantId { get; init; }
        public string? ClientId { get; init; }
        public string? ClientSecretRef { get; init; }
        public string? ClientSecret { get; init; }
    }
}

/// <summary>Credenciais do client credentials no Entra: tenant do Entra, app e a REFERÊNCIA ao segredo.</summary>
internal sealed record D365AuthSettings(string? TenantId, string? ClientId, string? ClientSecretRef)
{
    /// <summary>Exige os três campos (o fluxo client credentials não funciona sem eles).</summary>
    public (string TenantId, string ClientId, string ClientSecretRef) RequireComplete()
    {
        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecretRef))
        {
            throw new ConnectorSettingsException("auth incompleta: informe tenantId, clientId e clientSecretRef.");
        }

        return (TenantId, ClientId, ClientSecretRef);
    }
}
