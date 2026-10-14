using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>Código da empresa e do contribuinte na plataforma — a tradução do estabelecimento do ERP.</summary>
internal sealed record AvalaraCompanyCodes(string CodigoEmpresa, string CodigoContribuinte);

/// <summary>
/// A credencial do tenant, como está na conta de plataforma: o identificador do cliente e o NOME do segredo no cofre.
/// O valor do segredo nunca passa por aqui — quem o lê é o provider de token.
/// </summary>
internal sealed record AvalaraClientCredential(string ClientId, string SecretName);

/// <summary>
/// A configuração da Avalara no perfil do tenant (change <c>platform-account-and-connectors</c>, grupo 4): a conta de
/// plataforma, com a credencial e o endpoint de token, e o conector do próprio adapter, achado pelo nome (D9), com o mapa
/// de URLs base por versão e a tabela <c>establishments</c>. O ambiente é o da instância, só contexto dos motivos (D6). Os
/// códigos da plataforma do estabelecimento próprio nunca vêm do ERP: vêm da tabela, quando ela tem o CNPJ, ou da listagem
/// da plataforma (change <c>platform-establishment-resolution</c>, D3). A tabela é sobreposição opcional — ausente, ela é
/// vazia — e, quando tem a entrada, ganha. A mesma regra diz qual parte da nota é a nossa quando a origem não diz (D5).
/// Quem escreve o motivo das recusas da plataforma é este tipo (D9), porque sabe o vocabulário dela e onde fica a
/// sobreposição.
/// <para>A credencial e as URLs vêm do perfil, sem fallback global (ADR-0027), e só por <c>https</c> (http só em loopback,
/// que é o mock). O endpoint de token é o da conta, obrigatório, e nunca derivado da URL base do conector (D11). O segredo
/// só é aceito como referência no prefixo do tenant; um segredo em claro nas settings do conector recusa tudo o que levaria
/// a uma requisição.</para>
/// <para>A leitura nunca lança; a falta de configuração vira <see cref="DispatchRejectedException"/> só quando o envio
/// precisa dela, e nenhum motivo repete um valor de segredo.</para>
/// </summary>
internal sealed class AvalaraOutboundSettings
{
    /// <summary>
    /// A versão de base que o envio fiscal usa: a declaração deste adapter. A chave é um rótulo, que escolhe a base e nunca
    /// entra na URL (D4).
    /// </summary>
    internal const string DispatchVersion = "v2";

    private const string Screen = "Configurações → Conectores → Avalara";
    private const string Name = AvalaraServiceCollectionExtensions.AdapterName;
    private const string PlatformName = AvalaraServiceCollectionExtensions.PlatformName;
    private const string TableAt = "na tabela establishments do conector '" + Name + "'";
    private const int NamedCandidates = 5;   // o motivo da duplicidade nomeia até cinco: legível, e longe do corte do registro

    private readonly Dictionary<string, JsonElement> _establishments = new(StringComparer.Ordinal);   // chave: CNPJ normalizado
    private readonly string? _accountProblem;       // sem perfil, ou sem a conta de plataforma: vale para a credencial e o token
    private readonly string? _connectorProblem;     // sem o conector, settings que não são objeto, ou com seção de ambiente
    // Os quatro abaixo são escritos só na construção, pela leitura das settings do conector.
    private string? _establishmentsProblem;   // a tabela que não é objeto; ausente, ela é a sobreposição vazia
    private string? _clearSecretProblem;   // segredo em claro nas settings do conector: recusa tudo o que faz requisição
    private Uri? _baseUri;
    private string? _baseUriProblem;
    private readonly Uri? _tokenUri;
    private readonly string? _tokenUriProblem;
    private readonly AvalaraClientCredential? _credential;
    private readonly string? _credentialProblem;
    private readonly string? _invoicesPath;      // o caminho da integração de notas, relativo à base, sem a barra do começo
    private readonly string? _invoicesProblem;   // sem a integração ligada, ou sem caminho: a integração inativa (D5)

    private AvalaraOutboundSettings(
        string tenantId, string environment, PlatformAccount? account, string? accountProblem, PlatformConnector? connector)
    {
        TenantId = tenantId;
        Environment = environment;
        _accountProblem = accountProblem;

        if (account is not null)
        {
            (_credential, _credentialProblem) = ReadCredential(account);
            if (account.TokenUrl is { Length: > 0 } tokenUrl)
            {
                (_tokenUri, _tokenUriProblem) = ParseUrl(tokenUrl, $"o tokenUrl da conta de plataforma '{PlatformName}'", endWithSlash: false);
            }
            else
            {
                _tokenUriProblem = $"o tenant '{tenantId}', no ambiente '{environment}' da instância, não tem o endpoint de token na "
                    + $"conta de plataforma '{PlatformName}' (tokenUrl). O hub não o deriva da URL base do conector. Configure em {Screen} → "
                    + "Endpoint de token.";
            }
        }

        _connectorProblem = connector is null
            ? accountProblem ?? $"o tenant '{tenantId}' não tem o conector '{Name}' na conta de plataforma. Configure em {Screen}."
            : ReadConnector(connector.Settings);

        // A integração de notas, no próprio conector (D9): ligada, com o módulo Fiscal, e com caminho. O caminho é o da
        // integração, e de nenhum outro lugar — nem do código, nem do appsettings (compliance-dispatch-outcome).
        ConnectorIntegration? invoices = connector?.Modules
            .Where(m => string.Equals(m.Module, TenantModules.Fiscal, StringComparison.Ordinal))
            .SelectMany(m => m.Integrations)
            .FirstOrDefault(i => string.Equals(i.Key, ConnectorIntegrations.Invoices, StringComparison.Ordinal));
        if (connector is not null && invoices is null)
        {
            _invoicesProblem = $"o conector '{Name}' do tenant '{tenantId}', no ambiente '{environment}' da instância, não tem a "
                + $"integração de notas fiscais ({ConnectorIntegrations.Invoices}) ligada — e ela só fica ligada com o módulo Fiscal. "
                + $"Ligue o módulo e a integração em {Screen}.";
        }
        else if (invoices is not null && string.IsNullOrWhiteSpace(invoices.Endpoint))
        {
            _invoicesProblem = $"a integração de notas fiscais ({ConnectorIntegrations.Invoices}) do conector '{Name}' do tenant "
                + $"'{tenantId}' está sem caminho, e fica inativa. Configure o caminho em {Screen} → Notas fiscais.";
        }
        else if (invoices is not null)
        {
            _invoicesPath = invoices.Endpoint!.Trim().TrimStart('/');
        }
    }

    public string TenantId { get; }

    /// <summary>O ambiente da instância (<c>Sandbox</c>, <c>Production</c>): só contexto dos motivos, e não escolha de seção.</summary>
    public string Environment { get; }

    /// <summary>A plataforma da conta: a do adapter.</summary>
    public string Platform => PlatformName;

    /// <summary>A URL base da versão do envio fiscal, no conector. Sem ela, ou fora da regra de <c>https</c>, é rejeição.</summary>
    public Uri BaseUri
    {
        get
        {
            Check(_connectorProblem, _clearSecretProblem, _baseUriProblem);
            return _baseUri!;
        }
    }

    /// <summary>
    /// O envio de notas: a base da versão que a integração usa, mais o caminho dela. A chave da versão escolhe a base e não
    /// entra na URL (D4). Sem a integração ligada, ou sem caminho, é rejeição de configuração, sem requisição (D5, D9).
    /// </summary>
    public Uri SubmitUri
    {
        get
        {
            Check(_connectorProblem, _clearSecretProblem, _invoicesProblem, _baseUriProblem);
            return new Uri(_baseUri!, _invoicesPath!);
        }
    }

    /// <summary>A consulta de status: a base da versão, o caminho da integração, o identificador e <c>/status</c>.</summary>
    public Uri StatusUri(string externalId)
    {
        Check(_connectorProblem, _clearSecretProblem, _invoicesProblem, _baseUriProblem);
        return new Uri(_baseUri!, $"{_invoicesPath!.TrimEnd('/')}/{Uri.EscapeDataString(externalId)}/status");
    }

    /// <summary>A credencial da conta: o <c>clientId</c> e o nome do segredo, sempre no prefixo do tenant.</summary>
    public AvalaraClientCredential Credential
    {
        get
        {
            Check(_accountProblem, _clearSecretProblem, _credentialProblem);
            return _credential!;
        }
    }

    /// <summary>O endpoint de token da conta. Obrigatório, sem derivação da URL base do conector (D11).</summary>
    public Uri TokenEndpoint
    {
        get
        {
            Check(_accountProblem, _clearSecretProblem, _tokenUriProblem);
            return _tokenUri!;
        }
    }

    /// <summary>A rejeição do Client Secret que não está configurado, pela falta da referência ou do valor no cofre.</summary>
    public DispatchRejectedException SecretNotConfigured(bool vaultLacksValue) => Rejected(SecretNotConfiguredText(vaultLacksValue));

    /// <summary>
    /// A rejeição do 404 no envio: o caminho de envio não existe na URL montada. Ela tem duas partes, as duas na tela de
    /// conectores — a URL base da versão, no conector, e o caminho da integração de notas —, e o motivo aponta as duas. Só
    /// vale para o envio: na consulta de status, o 404 é o documento ainda não indexado.
    /// </summary>
    public DispatchRejectedException SubmitPathNotFound(Uri url, string? platformReason)
        => Rejected($"o caminho de envio não existe nessa URL (HTTP 404 em POST {url.GetLeftPart(UriPartial.Path)}). A URL tem "
            + $"duas partes, as duas na tela de conectores: a URL base da {DispatchVersion} do conector '{Name}' do tenant "
            + $"'{TenantId}' ({BaseUri}; configure em {Screen} → URL base ({DispatchVersion})) e o caminho da integração de notas "
            + $"fiscais ('{_invoicesPath}'; configure em {Screen} → Notas fiscais)."
            + (string.IsNullOrWhiteSpace(platformReason) ? string.Empty : $" Resposta da plataforma: {platformReason}"));

    /// <summary>
    /// A rejeição de um 404 num caminho da listagem: a URL tem duas partes, em dois lugares — o host na URL base do conector,
    /// e o caminho numa opção do appsettings, porque a listagem não é uma integração (D9) —, e o motivo aponta os dois, sem a
    /// query.
    /// </summary>
    public DispatchRejectedException PathNotFound(string what, string method, Uri url, string option, string path, string? platformReason)
        => Rejected($"o caminho {what} não existe nessa URL (HTTP 404 em {method} {url.GetLeftPart(UriPartial.Path)}). A URL tem "
            + $"duas partes: a URL base da {DispatchVersion} do conector '{Name}' do tenant '{TenantId}' ({BaseUri}; configure em "
            + $"{Screen} → URL base ({DispatchVersion})) e o caminho {what} ('{path}', em {option}, no appsettings do host)."
            + (string.IsNullOrWhiteSpace(platformReason) ? string.Empty : $" Resposta da plataforma: {platformReason}"));

    /// <summary>
    /// A configuração da Avalara no perfil: a conta da plataforma e o conector do adapter, os dois pelo nome. O ambiente da
    /// instância vem como contexto dos motivos.
    /// </summary>
    public static AvalaraOutboundSettings Read(string tenantId, TenantConnectorProfile? profile, IInstanceEnvironment instance)
    {
        string environment = instance.Name;
        if (profile is null)
        {
            return new(tenantId, environment, null, $"o tenant '{tenantId}' não tem perfil de conector.", null);
        }

        PlatformAccount? account = profile.Accounts.FirstOrDefault(a => string.Equals(a.Platform, PlatformName, StringComparison.Ordinal));
        PlatformConnector? connector = account?.Connectors.FirstOrDefault(c => string.Equals(c.Adapter, Name, StringComparison.Ordinal));
        string? accountProblem = account is null
            ? $"o tenant '{tenantId}' não tem a conta de plataforma '{PlatformName}'. Configure em {Screen}."
            : null;
        return new(tenantId, environment, account, accountProblem, connector);
    }

    // As settings do conector: o mapa de bases, a tabela e o segredo em claro. Uma seção por ambiente, que só chega por SQL
    // direto, recusa tudo: as settings têm um nível só, e ler a seção seria escolher um ambiente que a instância não é.
    private string? ReadConnector(string? settings)
    {
        JsonElement root;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(settings) ? "{}" : settings);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return $"as settings do conector '{Name}' do tenant '{TenantId}' não são um JSON válido.";
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return $"as settings do conector '{Name}' do tenant '{TenantId}' não são um objeto JSON.";
        }

        if (root.EnumerateObject().Select(p => p.Name).FirstOrDefault(IsEnvironmentSection) is { } section)
        {
            return $"as settings do conector '{Name}' do tenant '{TenantId}' ainda têm a seção '{section}', por ambiente: as "
                + $"settings têm um nível só, e o ambiente é o da instância ('{Environment}'). Grave-as de novo pela tela, em {Screen}.";
        }

        if (root.TryGetProperty("establishments", out JsonElement table) && table.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (table.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in table.EnumerateObject())
                {
                    _establishments[TaxIdentifiers.Normalize(entry.Name)] = entry.Value.Clone();
                }
            }
            else
            {
                _establishmentsProblem = $"a tabela establishments do conector '{Name}' do tenant '{TenantId}' não é um objeto (a "
                    + "tradução do CNPJ do estabelecimento para codigoEmpresa e codigoContribuinte).";
            }
        }

        if (FindClearSecret(root, string.Empty) is { } field)
        {
            _clearSecretProblem = $"o segredo {field} está em claro nas settings do conector '{Name}', e só é aceito como referência "
                + $"do cofre. Grave-o pela tela, em {Screen}.";
        }

        string? baseUrl = root.TryGetProperty("baseUrls", out JsonElement bases) ? Text(bases, DispatchVersion) : null;
        if (baseUrl is null)
        {
            _baseUriProblem = $"o conector '{Name}' do tenant '{TenantId}', no ambiente '{Environment}' da instância, não tem a URL "
                + $"base da versão {DispatchVersion} (baseUrls.{DispatchVersion}). Configure em {Screen} → URL base ({DispatchVersion}).";
        }
        else
        {
            (_baseUri, _baseUriProblem) = ParseUrl(baseUrl, $"a URL base da versão {DispatchVersion} do conector '{Name}' (baseUrls.{DispatchVersion})", endWithSlash: true);
        }

        return null;
    }

    /// <summary>Lança a rejeição de configuração da tabela (sem perfil, sem conector, tabela que não é objeto).</summary>
    public void ThrowIfUnreadable() => Table();

    /// <summary>A tabela tem uma entrada para o CNPJ, completa ou não.</summary>
    public bool HasOverride(string taxId) => Table().ContainsKey(TaxIdentifiers.Normalize(taxId));

    /// <summary>
    /// Os códigos da entrada da tabela para o estabelecimento próprio, ou <c>null</c> sem entrada. A entrada incompleta é
    /// rejeitada nomeando o campo, e nunca completada pela plataforma: quem a escreveu disse qual é a tradução (D3).
    /// </summary>
    public AvalaraCompanyCodes? OverrideFor(string establishmentTaxId)
    {
        Dictionary<string, JsonElement> table = Table();
        string cnpj = TaxIdentifiers.Normalize(establishmentTaxId);
        if (!table.TryGetValue(cnpj, out JsonElement entry))
        {
            return null;
        }

        string? empresa = Text(entry, "codigoEmpresa");
        string? contribuinte = Text(entry, "codigoContribuinte");
        string[] missing = [.. new[] { ("codigoEmpresa", empresa), ("codigoContribuinte", contribuinte) }.Where(f => f.Item2 is null).Select(f => f.Item1)];
        if (missing.Length > 0)
        {
            throw Rejected(
                $"o tenant '{TenantId}', no ambiente '{Environment}' da instância, tem o estabelecimento {cnpj} sem "
                + $"{string.Join(" e ", missing)} {TableAt} (entrada \"{cnpj}\").");
        }

        return new AvalaraCompanyCodes(empresa!, contribuinte!);
    }

    /// <summary>
    /// Os códigos do estabelecimento próprio pela plataforma: o <c>codigoCIA</c> da empresa e o <c>codigo</c> do único
    /// contribuinte com o CNPJ. Nenhum ou mais de um é recusa, nunca escolha (D4, D9). Sem a listagem (o destino que não
    /// lista), a recusa de sempre, de falta de tradução na tabela.
    /// </summary>
    public AvalaraCompanyCodes CodesFromPlatform(string establishmentTaxId, PlatformEstablishmentIndex index)
    {
        Table();
        string cnpj = TaxIdentifiers.Normalize(establishmentTaxId);
        if (!index.CanList)
        {
            throw Rejected(
                $"o tenant '{TenantId}', no ambiente '{Environment}' da instância, não tem tradução para o estabelecimento {cnpj} "
                + $"(faltam codigoEmpresa e codigoContribuinte {TableAt}).");
        }

        return index.Match(cnpj) switch
        {
            EstablishmentMatch.Unique unique => CodesOf(cnpj, unique.Establishment),
            EstablishmentMatch.Ambiguous ambiguous => throw Rejected(
                $"o estabelecimento {cnpj} tem {ambiguous.Candidates.Count} contribuintes na plataforma (tenant '{TenantId}', ambiente "
                + $"'{Environment}'), e o hub não escolhe entre eles: "
                + string.Join("; ", ambiguous.Candidates.Take(NamedCandidates).Select(Candidate))
                + (ambiguous.Candidates.Count > NamedCandidates ? $"; e mais {ambiguous.Candidates.Count - NamedCandidates}" : string.Empty)
                + $". Remova a duplicidade na plataforma, ou traduza o estabelecimento {TableAt}."),
            _ => throw Rejected(
                $"o estabelecimento {cnpj} não tem contribuinte cadastrado na plataforma (tenant '{TenantId}', ambiente '{Environment}'). "
                + $"Cadastre-o na plataforma, ou traduza-o {TableAt}. Se o cadastro acabou de "
                + "ser feito, salve o perfil do conector (Configurações → Conectores) para o hub reler a plataforma, e reprocesse a nota."),
        };
    }

    /// <summary>
    /// Os códigos para o estabelecimento próprio: a entrada da tabela, se houver, e senão a plataforma. Sem índice, o
    /// destino é tratado como o que não lista.
    /// </summary>
    public AvalaraCompanyCodes CodesFor(string establishmentTaxId, PlatformEstablishmentIndex? index = null)
        => OverrideFor(establishmentTaxId) ?? CodesFromPlatform(establishmentTaxId, index ?? PlatformEstablishmentIndex.Unsupported);

    /// <summary>
    /// O estabelecimento próprio e o parceiro (a contraparte). A nota diz pela emissão própria ou de terceiros; sem isso,
    /// a nossa é a única parte que é estabelecimento do tenant — com entrada na tabela ou contribuinte na plataforma (D5).
    /// Nenhuma ou as duas é rejeição citando os dois CNPJs. Sem índice, só a tabela, como no destino que não lista.
    /// </summary>
    public (Party Own, Party Partner) PartiesOf(GoodsInvoice invoice, PlatformEstablishmentIndex? index = null)
    {
        switch (invoice.Issuance)
        {
            case Issuance.Own:
                return (invoice.Issuer, invoice.Recipient);
            case Issuance.ThirdParty:
                return (invoice.Recipient, invoice.Issuer);
        }

        Dictionary<string, JsonElement> table = Table();
        index ??= PlatformEstablishmentIndex.Unsupported;
        string issuer = TaxIdentifiers.Normalize(invoice.Issuer.TaxId), recipient = TaxIdentifiers.Normalize(invoice.Recipient.TaxId);
        bool issuerIsOurs = table.ContainsKey(issuer) || index.Knows(issuer);
        bool recipientIsOurs = table.ContainsKey(recipient) || index.Knows(recipient);
        return (issuerIsOurs, recipientIsOurs) switch
        {
            (true, false) => (invoice.Issuer, invoice.Recipient),
            (false, true) => (invoice.Recipient, invoice.Issuer),
            (true, true) when index.CanList => throw Rejected(
                $"as duas partes da nota (emitente {issuer}, destinatário {recipient}) são estabelecimentos do tenant '{TenantId}' "
                + $"(pela tradução {TableAt} ou pela plataforma), e a nota não diz qual é o estabelecimento próprio."),
            (true, true) => throw Rejected(
                $"as duas partes da nota (emitente {issuer}, destinatário {recipient}) estão {TableAt} "
                + "e a nota não diz qual é o estabelecimento próprio."),
            _ when index.CanList => throw Rejected(
                $"nenhuma parte da nota (emitente {issuer}, destinatário {recipient}) tem tradução {TableAt} nem contribuinte na "
                + $"plataforma, no tenant '{TenantId}' (ambiente '{Environment}')."),
            _ => throw Rejected(
                $"nenhuma parte da nota (emitente {issuer}, destinatário {recipient}) está {TableAt}, do tenant '{TenantId}'."),
        };
    }

    // O único contribuinte do CNPJ, com os dois códigos. O que falta não é preenchido com outro valor: nem o CNPJ, nem o
    // identificador, nem um dado do ERP.
    private AvalaraCompanyCodes CodesOf(string cnpj, PlatformEstablishment establishment)
    {
        if (establishment.CompanyCode is { } company && establishment.EstablishmentCode is { } code)
        {
            return new AvalaraCompanyCodes(company, code);
        }

        string[] missing = [.. new[]
        {
            (establishment.CompanyCode, "o código da empresa (codigoCIA)"),
            (establishment.EstablishmentCode, "o código do contribuinte (codigo)"),
        }.Where(f => f.Item1 is null).Select(f => f.Item2)];
        throw Rejected(
            $"o único contribuinte do estabelecimento {cnpj} na plataforma ({Candidate(establishment)}) está sem "
            + $"{string.Join(" e ", missing)} (tenant '{TenantId}', ambiente '{Environment}'). O hub não preenche o código com outro "
            + $"valor: corrija o cadastro na plataforma, ou traduza o estabelecimento {TableAt}.");
    }

    // Um candidato como o motivo o nomeia: a empresa pelo código e pela descrição, o contribuinte pelo código e pelo #id —
    // que separa dois contribuintes de mesmo código na mesma empresa, e é o endereço do registro na plataforma (D9). O
    // empresaId não entra: depois do código e da descrição, ele não acrescenta nada legível.
    private static string Candidate(PlatformEstablishment establishment)
        => (establishment.CompanyCode is { } company ? $"empresa '{company}'" : "empresa sem código")
            + (establishment.CompanyName is { } name ? $" ({name})" : string.Empty)
            + (establishment.EstablishmentCode is { } code ? $", contribuinte '{code}'" : ", contribuinte sem código")
            + (establishment.PlatformId is { } id ? $" (#{id})" : string.Empty);

    private static bool IsEnvironmentSection(string name)
        => name.Equals(InstanceEnvironment.Sandbox, StringComparison.OrdinalIgnoreCase)
            || name.Equals(InstanceEnvironment.Production, StringComparison.OrdinalIgnoreCase);

    private string SecretNotConfiguredText(bool vaultLacksValue)
        => $"o Client Secret da conta de plataforma '{PlatformName}' do tenant '{TenantId}', no ambiente '{Environment}' da instância, "
            + "não está configurado"
            + (vaultLacksValue ? " (o cofre não tem o valor; o emulador de dev pode ter reiniciado)" : string.Empty)
            + $". Configure em {Screen} → Client Secret.";

    private Dictionary<string, JsonElement> Table()
    {
        Check(_connectorProblem, _establishmentsProblem);
        return _establishments;
    }

    // O primeiro problema da lista vira rejeição.
    private static void Check(params string?[] problems)
    {
        if (problems.FirstOrDefault(p => p is not null) is { } problem)
        {
            throw Rejected(problem);
        }
    }

    private (AvalaraClientCredential?, string?) ReadCredential(PlatformAccount account)
    {
        string? clientId = string.IsNullOrWhiteSpace(account.ClientId) ? null : account.ClientId;
        string? reference = account.ClientSecretRef;
        string toSecret = $"Grave o Client Secret de novo pela tela, em {Screen} → Client Secret.";

        if (clientId is null && reference is null)
        {
            return (null, $"o tenant '{TenantId}', no ambiente '{Environment}' da instância, não tem clientId e Client Secret "
                + $"configurados na conta de plataforma '{PlatformName}'. Configure em {Screen}.");
        }

        if (reference is null)
        {
            return (null, SecretNotConfiguredText(vaultLacksValue: false));
        }

        // Nenhum dos dois motivos repete a referência: malformada, ela pode ser o próprio valor; de outro tenant, ela
        // nomeia o outro tenant.
        if (!SecretReference.TryParse(reference, out string? name))
        {
            return (null, $"a referência do Client Secret da conta de plataforma '{PlatformName}' (clientSecretRef) não é uma referência de "
                + $"cofre válida. {toSecret}");
        }

        if (!SecretNames.BelongsTo(name, TenantId))
        {
            return (null, $"a referência do Client Secret da conta de plataforma '{PlatformName}' (clientSecretRef) aponta para um segredo "
                + $"fora do prefixo do tenant '{TenantId}'. {toSecret}");
        }

        if (clientId is null)
        {
            return (null, $"o tenant '{TenantId}', no ambiente '{Environment}' da instância, não tem clientId configurado na conta "
                + $"de plataforma '{PlatformName}'. Configure em {Screen} → Client ID.");
        }

        return (new AvalaraClientCredential(clientId, name), null);
    }

    // https sempre; http só em loopback (o mock local e o teste em memória). O motivo não repete a URL.
    // A URL base termina sempre em barra: os caminhos (DocumentsPath, os da listagem) são relativos a ela, e sem a barra o
    // último segmento seria trocado ("…/avalara" + "documents" = "…/documents"). O tokenUrl é a URL completa do endpoint, e
    // fica como veio.
    private static (Uri?, string?) ParseUrl(string raw, string field, bool endWithSlash)
    {
        if (!ConnectorUrls.TryParse(raw, out Uri? uri))
        {
            return (null, $"{field} {ConnectorUrls.Rule}.");
        }

        return endWithSlash && !uri.AbsolutePath.EndsWith('/')
            ? (new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri, null)
            : (uri, null);
    }

    // Um campo de escrita com valor, em qualquer nível das settings: só chega aqui por SQL direto ou seed (ADR-0027).
    private static string? FindClearSecret(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string field = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
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

    internal static DispatchRejectedException Rejected(string what) => new($"Configuração do conector: {what}");

    private static string? Text(JsonElement entry, string name)
        => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out JsonElement v)
           && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : null;

}
