using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>Código da empresa e do contribuinte na plataforma — a tradução do estabelecimento do ERP.</summary>
internal sealed record AvalaraCompanyCodes(string CodigoEmpresa, string CodigoContribuinte);

/// <summary>
/// A credencial do tenant no ambiente, como está nas settings: o identificador do cliente e o NOME do segredo no cofre.
/// O valor do segredo nunca passa por aqui — quem o lê é o provider de token.
/// </summary>
internal sealed record AvalaraClientCredential(string ClientId, string SecretName);

/// <summary>
/// Settings de saída da Avalara no perfil do tenant, na seção do ambiente ativo (design D4). A tabela
/// <c>establishments</c> traduz o CNPJ do estabelecimento próprio para os códigos da plataforma — requisito permanente,
/// não ajuste de demo: todo cliente tem código diferente entre o ERP e a plataforma. A mesma tabela diz qual parte da
/// nota é a nossa quando a origem não diz (D5).
/// <para>A credencial e as URLs vêm da mesma seção, sem fallback global (ADR-0027): a credencial de um tenant só vai ao
/// endereço do ambiente dele, e só por <c>https</c> (http só em loopback, que é o mock). O segredo só é aceito como
/// referência no prefixo do tenant; um segredo em claro na seção recusa tudo o que levaria a uma requisição.</para>
/// <para>A leitura nunca lança; a falta de configuração vira <see cref="DispatchRejectedException"/> só quando o envio
/// precisa dela, e nenhum motivo repete um valor de segredo.</para>
/// </summary>
internal sealed class AvalaraOutboundSettings
{
    private const string Screen = "Configurações → Conectores → Avalara";

    private readonly Dictionary<string, JsonElement>? _establishments;   // chave: CNPJ só com dígitos
    private readonly string? _problem;              // sem perfil, sem seção ou JSON inválido: vale para tudo
    private readonly string? _establishmentsProblem;
    private readonly string? _clearSecretProblem;   // segredo em claro na seção: recusa tudo o que faz requisição
    private readonly Uri? _baseUri;
    private readonly string? _baseUriProblem;
    private readonly Uri? _tokenUri;                // null: baseUrl + caminho do token
    private readonly string? _tokenUriProblem;
    private readonly AvalaraClientCredential? _credential;
    private readonly string? _credentialProblem;

    private AvalaraOutboundSettings(string tenantId, string environment, string problem)
    {
        TenantId = tenantId;
        Environment = environment;
        _problem = problem;
    }

    private AvalaraOutboundSettings(string tenantId, string environment, JsonElement section)
    {
        TenantId = tenantId;
        Environment = environment;
        string where = $"OutboundSettings.{environment}";

        if (section.TryGetProperty("establishments", out JsonElement table) && table.ValueKind == JsonValueKind.Object)
        {
            _establishments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty entry in table.EnumerateObject())
            {
                _establishments[TaxIdentifiers.Normalize(entry.Name)] = entry.Value.Clone();
            }
        }
        else
        {
            _establishmentsProblem = $"o tenant '{tenantId}', no ambiente '{environment}', não tem {where}.establishments "
                + "(a tradução do CNPJ do estabelecimento para codigoEmpresa e codigoContribuinte).";
        }

        if (FindClearSecret(section, where) is { } field)
        {
            _clearSecretProblem = $"o segredo {field} está em claro nas settings gravadas, e só é aceito como referência do cofre. "
                + $"Grave-o pela tela, em {Screen} → {EnvironmentLabel}.";
        }

        string? baseUrl = Text(section, "baseUrl");
        if (baseUrl is null)
        {
            _baseUriProblem = $"o tenant '{tenantId}', no ambiente '{environment}', não tem baseUrl ({where}.baseUrl). "
                + $"Configure em {Screen} → {EnvironmentLabel} → URL base.";
        }
        else
        {
            (_baseUri, _baseUriProblem) = ParseUrl(baseUrl, $"{where}.baseUrl", endWithSlash: true);
        }

        if (Text(section, "tokenUrl") is { } tokenUrl)
        {
            (_tokenUri, _tokenUriProblem) = ParseUrl(tokenUrl, $"{where}.tokenUrl", endWithSlash: false);
        }

        (_credential, _credentialProblem) = ReadCredential(section, where);
    }

    public string TenantId { get; }

    /// <summary>O ambiente ativo, em minúsculas: o nome da seção (<c>sandbox</c>, <c>production</c>).</summary>
    public string Environment { get; }

    /// <summary>A URL base do ambiente ativo. Sem ela, ou fora da regra de <c>https</c>, é rejeição — nunca fallback.</summary>
    public Uri BaseUri
    {
        get
        {
            Check(_problem, _clearSecretProblem, _baseUriProblem);
            return _baseUri!;
        }
    }

    /// <summary>A credencial do ambiente ativo: o <c>clientId</c> e o nome do segredo, sempre no prefixo do tenant.</summary>
    public AvalaraClientCredential Credential
    {
        get
        {
            Check(_problem, _clearSecretProblem, _credentialProblem);
            return _credential!;
        }
    }

    /// <summary>O endpoint de token: o <c>tokenUrl</c> da seção, ou a URL base mais o caminho de token da plataforma.</summary>
    public Uri TokenEndpoint(string tokenPath)
    {
        Check(_problem, _clearSecretProblem, _baseUriProblem, _tokenUriProblem);
        return _tokenUri ?? new Uri(_baseUri!, tokenPath);
    }

    /// <summary>A rejeição do Client Secret que não está configurado, pela falta da referência ou do valor no cofre.</summary>
    public DispatchRejectedException SecretNotConfigured(bool vaultLacksValue) => Rejected(SecretNotConfiguredText(vaultLacksValue));

    /// <summary>
    /// A rejeição do 404 no envio: o caminho de envio não existe na URL montada. Ela tem duas partes, em dois lugares — o
    /// host na URL base da seção do perfil (na tela) e o caminho em <c>Avalara:DocumentsPath</c> (no appsettings) —, e o
    /// motivo aponta os dois. Só vale para o envio: na consulta de status, o 404 é o documento ainda não indexado.
    /// </summary>
    public DispatchRejectedException SubmitPathNotFound(Uri url, string documentsPath, string? platformReason)
        => Rejected($"o caminho de envio não existe nessa URL (HTTP 404 em POST {url.GetLeftPart(UriPartial.Path)}). A URL tem "
            + $"duas partes: a URL base do ambiente '{Environment}' do tenant '{TenantId}' ({BaseUri}, em "
            + $"OutboundSettings.{Environment}.baseUrl; configure em {Screen} → {EnvironmentLabel} → URL base) e o caminho de "
            + $"envio ('{documentsPath}', em Avalara:DocumentsPath, no appsettings do host)."
            + (string.IsNullOrWhiteSpace(platformReason) ? string.Empty : $" Resposta da plataforma: {platformReason}"));

    public static AvalaraOutboundSettings Read(string tenantId, TenantConnectorProfile? profile)
    {
        if (profile is null)
        {
            return new(tenantId, "?", $"o tenant '{tenantId}' não tem perfil de conector.");
        }

        string environment = profile.Environment.ToLowerInvariant();   // "sandbox" / "production"
        try
        {
            using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(profile.OutboundSettings) ? "{}" : profile.OutboundSettings);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(environment, out JsonElement section)
                || section.ValueKind != JsonValueKind.Object)
            {
                return new(tenantId, environment, $"o tenant '{tenantId}' não tem a seção '{environment}' (ambiente ativo) em OutboundSettings.");
            }

            return new(tenantId, environment, section.Clone());
        }
        catch (JsonException)
        {
            return new(tenantId, environment, $"as OutboundSettings do tenant '{tenantId}' não são um JSON válido.");
        }
    }

    /// <summary>
    /// Os códigos da plataforma para o estabelecimento próprio. Nunca vêm do ERP: sem tradução, o envio é rejeitado
    /// nomeando o que falta.
    /// </summary>
    public AvalaraCompanyCodes CodesFor(string establishmentTaxId)
    {
        Dictionary<string, JsonElement> table = Table();
        string cnpj = TaxIdentifiers.Normalize(establishmentTaxId);
        string where = $"OutboundSettings.{Environment}.establishments";

        if (!table.TryGetValue(cnpj, out JsonElement entry))
        {
            throw Rejected(
                $"o tenant '{TenantId}', no ambiente '{Environment}', não tem tradução para o estabelecimento {cnpj} "
                + $"(faltam codigoEmpresa e codigoContribuinte em {where}).");
        }

        string? empresa = Text(entry, "codigoEmpresa");
        string? contribuinte = Text(entry, "codigoContribuinte");
        string[] missing = [.. new[] { ("codigoEmpresa", empresa), ("codigoContribuinte", contribuinte) }.Where(f => f.Item2 is null).Select(f => f.Item1)];
        if (missing.Length > 0)
        {
            throw Rejected(
                $"o tenant '{TenantId}', no ambiente '{Environment}', tem o estabelecimento {cnpj} sem {string.Join(" e ", missing)} "
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
        string issuer = TaxIdentifiers.Normalize(invoice.Issuer.TaxId), recipient = TaxIdentifiers.Normalize(invoice.Recipient.TaxId);
        bool issuerIsOurs = table.ContainsKey(issuer), recipientIsOurs = table.ContainsKey(recipient);

        return (issuerIsOurs, recipientIsOurs) switch
        {
            (true, false) => (invoice.Issuer, invoice.Recipient),
            (false, true) => (invoice.Recipient, invoice.Issuer),
            (true, true) => throw Rejected(
                $"as duas partes da nota (emitente {issuer}, destinatário {recipient}) estão em OutboundSettings.{Environment}.establishments "
                + "e a nota não diz qual é o estabelecimento próprio."),
            _ => throw Rejected(
                $"nenhuma parte da nota (emitente {issuer}, destinatário {recipient}) está em OutboundSettings.{Environment}.establishments "
                + $"do tenant '{TenantId}'."),
        };
    }

    // O rótulo do ambiente na tela de conectores.
    private string EnvironmentLabel => Environment switch
    {
        "sandbox" => "Sandbox",
        "production" => "Produção",
        _ => Environment,
    };

    private string SecretNotConfiguredText(bool vaultLacksValue)
        => $"o Client Secret do ambiente '{Environment}' do tenant '{TenantId}' não está configurado "
            + $"(OutboundSettings.{Environment}.clientSecret)"
            + (vaultLacksValue ? " (o cofre não tem o valor; o emulador de dev pode ter reiniciado)" : string.Empty)
            + $". Configure em {Screen} → {EnvironmentLabel} → Client Secret.";

    private Dictionary<string, JsonElement> Table()
    {
        Check(_problem, _establishmentsProblem);
        return _establishments!;
    }

    // O primeiro problema da lista vira rejeição.
    private static void Check(params string?[] problems)
    {
        if (problems.FirstOrDefault(p => p is not null) is { } problem)
        {
            throw Rejected(problem);
        }
    }

    private (AvalaraClientCredential?, string?) ReadCredential(JsonElement section, string where)
    {
        string? clientId = Text(section, "clientId");
        bool hasReference = section.TryGetProperty("clientSecretRef", out JsonElement reference) && reference.ValueKind != JsonValueKind.Null;
        string toSecret = $"Grave o Client Secret de novo pela tela, em {Screen} → {EnvironmentLabel} → Client Secret.";

        if (clientId is null && !hasReference)
        {
            return (null, $"o tenant '{TenantId}', no ambiente '{Environment}', não tem clientId e Client Secret configurados ({where}). "
                + $"Configure em {Screen} → {EnvironmentLabel}.");
        }

        if (!hasReference)
        {
            return (null, SecretNotConfiguredText(vaultLacksValue: false));
        }

        // Nenhum dos dois motivos repete a referência: malformada, ela pode ser o próprio valor; de outro tenant, ela
        // nomeia o outro tenant.
        if (!SecretReference.TryParse(reference.ValueKind == JsonValueKind.String ? reference.GetString() : null, out string? name))
        {
            return (null, $"{where}.clientSecretRef não é uma referência de cofre válida. {toSecret}");
        }

        if (!SecretNames.BelongsTo(name, TenantId))
        {
            return (null, $"{where}.clientSecretRef aponta para um segredo fora do prefixo do tenant '{TenantId}'. {toSecret}");
        }

        if (clientId is null)
        {
            return (null, $"o tenant '{TenantId}', no ambiente '{Environment}', não tem clientId configurado ({where}.clientId). "
                + $"Configure em {Screen} → {EnvironmentLabel} → Client ID.");
        }

        return (new AvalaraClientCredential(clientId, name), null);
    }

    // https sempre; http só em loopback (o mock local e o teste em memória). O motivo não repete a URL.
    // A URL base termina sempre em barra: os caminhos (DocumentsPath, TokenPath) são relativos a ela, e sem a barra o último
    // segmento seria trocado ("…/avalara" + "documents" = "…/documents"). O tokenUrl é a URL completa do endpoint, e fica
    // como veio.
    private static (Uri?, string?) ParseUrl(string raw, string field, bool endWithSlash)
    {
        bool valid = Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
        if (!valid)
        {
            return (null, $"{field} precisa ser uma URL https absoluta (http só em loopback).");
        }

        return endWithSlash && !uri!.AbsolutePath.EndsWith('/')
            ? (new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri, null)
            : (uri, null);
    }

    // Um campo de escrita com valor, em qualquer nível da seção: só chega aqui por SQL direto ou seed (ADR-0027).
    private static string? FindClearSecret(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string field = $"{path}.{property.Name}";
                    if (ConnectorSecretFields.IsWriteField(property.Name))
                    {
                        if (property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                            && !(property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: 0 }))
                        {
                            return field;
                        }
                    }
                    else if (FindClearSecret(property.Value, field) is { } found)
                    {
                        return found;
                    }
                }

                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (FindClearSecret(item, $"{path}[{i++}]") is { } found)
                    {
                        return found;
                    }
                }

                break;
        }

        return null;
    }

    private static DispatchRejectedException Rejected(string what) => new($"Configuração do conector: {what}");

    private static string? Text(JsonElement entry, string name)
        => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out JsonElement v)
           && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : null;

}
