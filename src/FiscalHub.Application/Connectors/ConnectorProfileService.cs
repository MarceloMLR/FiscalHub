using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Connectors;

/// <summary>
/// Gravação e leitura do perfil de conector do tenant logado (ADR-0027, ADR-0028). O segredo entra pela tela como
/// campo de escrita, vai para o cofre sob o nome que o servidor deriva, e o perfil guarda só a referência.
/// <para>A saída é a lista de contas de plataforma, cada uma com a credencial e os conectores dela (ADR-0036). A credencial
/// da plataforma é da conta: nas settings de um conector, ela é recusada. Uma gravação sem as contas mantém as gravadas,
/// e uma conta sem os conectores mantém os dela (change <c>platform-account-and-connectors</c>, grupo 4).</para>
/// <para>Ordem da gravação: valida tudo antes de escrever qualquer coisa; grava os segredos no cofre; grava o perfil; e
/// avisa os observadores sempre que o cofre ou o perfil mudou — mesmo que o perfil falhe depois de uma escrita no cofre,
/// porque o nome é fixo por caminho e o segredo já mudou.</para>
/// </summary>
public sealed class ConnectorProfileService
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IConnectorProfileStore _profiles;
    private readonly ISecretStore _secrets;
    private readonly IEnumerable<IConnectorProfileObserver> _observers;
    private readonly IReadOnlyList<IAdapterDeclaration> _declarations;
    private readonly ITenantContext _tenant;

    public ConnectorProfileService(
        IConnectorProfileStore profiles,
        ISecretStore secrets,
        IEnumerable<IConnectorProfileObserver> observers,
        IEnumerable<IAdapterDeclaration> declarations,
        ITenantContext tenant)
    {
        _profiles = profiles;
        _secrets = secrets;
        _observers = observers;
        _declarations = [.. declarations];
        _tenant = tenant;
    }

    public async Task<ConnectorProfileSaveResult> SaveAsync(ConnectorProfileRequest request, CancellationToken ct = default)
    {
        string tenantId = _tenant.TenantId;
        TenantConnectorProfile? stored = await _profiles.GetAsync(tenantId, ct);
        string? supportAdapter = request.SupportAdapter ?? stored?.SupportAdapter;

        var problems = new List<string>();
        string? storedInbound = StoredFor(stored?.InboundAdapter, request.InboundAdapter, stored?.InboundSettings);
        Prepared inbound = Prepare(tenantId, ConnectorSettingsKind.Inbound, request.InboundSettings, storedInbound, problems);
        AddPollProblems(inbound.Json, storedInbound, problems);
        var accountWrites = new List<SecretWrite>();
        IReadOnlyList<PlatformAccount> accounts = request.Accounts is null
            ? stored?.Accounts ?? []   // um dashboard que não recarregou não manda a conta: a saída não é apagada por isso
            : PrepareAccounts(tenantId, request.Accounts, stored?.Accounts ?? [], _declarations, accountWrites, problems);
        AddIntegrationUniquenessProblems(accounts, problems);
        Prepared support = request.SupportSettings is null
            ? new Prepared(stored?.SupportSettings ?? "{}", [])
            : Prepare(tenantId, ConnectorSettingsKind.Support, request.SupportSettings,
                StoredFor(stored?.SupportAdapter, supportAdapter, stored?.SupportSettings), problems);

        // A lista de módulos do tenant deixou de existir (D7): ausente, nada muda; presente, é um dashboard que não recarregou,
        // e o operador acreditaria ter configurado algo. Recusada, como a seção por ambiente nas settings.
        if (request.Modules is not null)
        {
            problems.Add("modules: a lista de módulos não é mais do tenant — os módulos são ligados em cada conector "
                + "(Accounts[].Connectors[].Modules). Marque-os no conector, e recarregue a tela se ela ainda mostra a aba Módulos.");
        }

        SecretWrite[] writes = [.. inbound.Writes, .. accountWrites, .. support.Writes];
        problems.AddRange(writes.GroupBy(w => w.Name).Where(g => g.Count() > 1)
            .Select(g => $"{string.Join(" e ", g.Select(w => w.Field))} caem no mesmo segredo do cofre: deixe um só."));

        if (problems.Count > 0)
        {
            return new ConnectorProfileSaveResult(ConnectorProfileSaveStatus.Invalid, problems);
        }

        int written = 0;
        bool upserted = false;
        try
        {
            foreach (SecretWrite write in writes)
            {
                try
                {
                    await _secrets.SetAsync(write.Name, write.Value, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A mensagem é nossa, e não a da exceção: ela cita o campo, e nunca o valor.
                    return new ConnectorProfileSaveResult(ConnectorProfileSaveStatus.SecretStoreFailed,
                        [$"O cofre não aceitou o segredo de {write.Field}, e o perfil não foi gravado. Tente de novo."]);
                }

                written++;
            }

            await _profiles.UpsertAsync(new TenantConnectorProfile
            {
                TenantId = tenantId,
                InboundAdapter = request.InboundAdapter,
                InboundSettings = inbound.Json,
                Accounts = accounts,
                SupportAdapter = supportAdapter,
                SupportSettings = support.Json,
                // Ausente mantém a gravada (ADR-0040): um dashboard que não recarregou não liga o envio de quem o desligou.
                OutboundDispatchEnabled = request.OutboundDispatch ?? stored?.OutboundDispatchEnabled ?? true,
            }, ct);
            upserted = true;

            return new ConnectorProfileSaveResult(ConnectorProfileSaveStatus.Saved, []);
        }
        finally
        {
            if (written > 0 || upserted)
            {
                // Sem o token da requisição: um cancelamento não pode deixar o cache velho de pé.
                foreach (IConnectorProfileObserver observer in _observers)
                {
                    await observer.ProfileSavedAsync(tenantId, CancellationToken.None);
                }
            }
        }
    }

    /// <summary>O perfil do tenant logado, mascarado. <c>null</c> se ele não tem perfil.</summary>
    public async Task<ConnectorProfileView?> GetAsync(CancellationToken ct = default)
    {
        string tenantId = _tenant.TenantId;
        TenantConnectorProfile? profile = await _profiles.GetAsync(tenantId, ct);
        if (profile is null)
        {
            return null;
        }

        var references = new List<(string Key, string? Value)>();
        string inbound = Mask("inbound", profile.InboundSettings, references);
        string support = Mask("support", profile.SupportSettings, references);

        // O segredo da conta aparece sempre no mapa, configurado ou não: é o campo que a tela da conta mostra.
        var accounts = new List<PlatformAccountView>();
        foreach (PlatformAccount account in profile.Accounts)
        {
            references.Add(($"platform.{account.Platform}.clientSecret", account.ClientSecretRef));
            accounts.Add(new PlatformAccountView(account.Platform, account.ClientId, account.TokenUrl,
                [.. account.Connectors.Select(c => new PlatformConnectorView(
                    c.Adapter, Mask($"connector.{c.Adapter}", c.Settings, references), [.. c.Modules.Select(m => m.Module)],
                    IntegrationsOf(account.Platform, c)))]));
        }

        var secrets = new Dictionary<string, SecretStatus>(StringComparer.Ordinal);
        foreach ((string key, string? value) in references)
        {
            secrets[key] = await StatusAsync(tenantId, value, ct);
        }

        return new ConnectorProfileView(profile.TenantId,
            profile.InboundAdapter, inbound, profile.SupportAdapter, support, secrets, TenantModules.Of(profile), accounts,
            profile.OutboundDispatchEnabled);
    }

    // No máximo um conector por integração, no tenant, contando todas as contas (6.4): dois é erro de configuração, e não
    // escolha em runtime nem "o primeiro ganha". O índice em (tenant, chave) é a rede de segurança; esta é a regra, e é ela
    // que diz quais são os dois.
    private static void AddIntegrationUniquenessProblems(IReadOnlyList<PlatformAccount> accounts, List<string> problems)
    {
        var where = accounts
            .SelectMany(a => a.Connectors.SelectMany(c => c.Modules.SelectMany(m => m.Integrations
                .Select(i => (i.Key, Place: $"'{c.Adapter}' (conta '{a.Platform}')")))));
        problems.AddRange(where.GroupBy(w => w.Key, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"Accounts: a integração '{g.Key}' está ligada em mais de um conector — {Listed([.. g.Select(w => w.Place)])} —, "
                + "e o tenant a liga num conector só. Desligue-a em um deles."));
    }

    // As integrações ligadas do conector, na leitura. O tamanho de lote só aparece onde o adapter declara que a API aceita
    // lote (6.6) — o que o SQL direto tenha posto noutra fica de fora —, e o caminho nulo ou em branco é a integração sem
    // caminho, inativa (D5).
    private IReadOnlyList<ConnectorIntegrationView> IntegrationsOf(string platform, PlatformConnector connector)
    {
        IAdapterDeclaration? declaration = _declarations.FirstOrDefault(d =>
            string.Equals(d.Platform, platform, StringComparison.Ordinal) && string.Equals(d.Adapter, connector.Adapter, StringComparison.Ordinal));
        HashSet<string> batched = declaration is null
            ? []
            : [.. declaration.Modules.SelectMany(m => m.Integrations).Where(i => i.AcceptsBatch).Select(i => i.Key)];
        return [.. connector.Modules.SelectMany(m => m.Integrations.Select(i => new ConnectorIntegrationView(
            i.Key, m.Module, i.Endpoint, batched.Contains(i.Key) ? i.BatchSize : null, string.IsNullOrWhiteSpace(i.Endpoint))))];
    }

    // As contas da gravação: uma por plataforma, com o nome que a implementação declara (D17); o Client Secret vai para o
    // cofre sob o nome da plataforma, e a conta guarda a referência; o ausente mantém a gravada. Os conectores ausentes
    // mantêm os gravados da conta.
    private static IReadOnlyList<PlatformAccount> PrepareAccounts(
        string tenantId, IReadOnlyList<PlatformAccountRequest> requested, IReadOnlyList<PlatformAccount> stored,
        IReadOnlyList<IAdapterDeclaration> declarations, List<SecretWrite> writes, List<string> problems)
    {
        problems.AddRange(requested
            .GroupBy(a => a.Platform?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Accounts: a plataforma '{g.Key}' aparece {g.Count()} vezes, e o tenant tem uma conta por plataforma."));

        var accounts = new List<PlatformAccount>();
        foreach (PlatformAccountRequest account in requested)
        {
            string requestedPlatform = account.Platform?.Trim() ?? string.Empty;
            if (requestedPlatform.Length == 0)
            {
                problems.Add("Accounts: uma conta veio sem a plataforma.");
                continue;
            }

            // A plataforma é gravada como a implementação a declara, e as leituras a comparam exata: casar sem distinguir
            // caixa aqui é o que as mantém achando o que foi gravado. O que ninguém declara é recusado, e não adivinhado.
            string[] platforms = [.. declarations.Select(d => d.Platform).Distinct(StringComparer.Ordinal)];
            string? platform = platforms.FirstOrDefault(p => string.Equals(p, requestedPlatform, StringComparison.OrdinalIgnoreCase));
            if (platform is null)
            {
                problems.Add($"Accounts[{requestedPlatform}]: nenhuma implementação de adapter declara a plataforma '{requestedPlatform}'. "
                    + $"As declaradas são: {Listed(platforms)}.");
                continue;
            }

            string label = $"Accounts[{platform}]";
            PlatformAccount? before = stored.FirstOrDefault(a => string.Equals(a.Platform, platform, StringComparison.OrdinalIgnoreCase));
            if (account.ClientSecretRef is not null)
            {
                problems.Add($"{label}.clientSecretRef: a referência de segredo não vem na requisição; mande o valor no campo de segredo, e o servidor grava a referência.");
            }

            string? tokenUrl = Blank(account.TokenUrl);
            if (tokenUrl is not null && !ConnectorUrls.TryParse(tokenUrl, out _))
            {
                problems.Add($"{label}.tokenUrl: o endpoint de token {ConnectorUrls.Rule}.");
            }

            string? secretRef = before?.ClientSecretRef;
            if (!string.IsNullOrEmpty(account.ClientSecret))
            {
                secretRef = AccountSecret(tenantId, platform, label, account.ClientSecret, writes, problems) ?? secretRef;
            }

            accounts.Add(new PlatformAccount
            {
                Platform = platform,
                ClientId = Blank(account.ClientId),
                ClientSecretRef = secretRef,
                TokenUrl = tokenUrl,
                Connectors = account.Connectors is null
                    ? before?.Connectors ?? []
                    : PrepareConnectors(tenantId, label, platform, account.Connectors, before?.Connectors ?? [], declarations, writes, problems),
            });
        }

        return accounts;
    }

    // O Client Secret da conta é campo tipado, e não blob: o valor vira a referência da conta pelo mesmo caminho do campo de
    // escrita das settings — máscara recusada, nome derivado pelo servidor (connector-secret-references).
    private static string? AccountSecret(
        string tenantId, string platform, string label, string secret, List<SecretWrite> writes, List<string> problems)
    {
        string field = $"{label}.clientSecret";
        if (ConnectorSecretFields.IsMask(secret))
        {
            problems.Add($"{field} veio com a máscara da tela, e não com o segredo. Digite o segredo, ou deixe o campo vazio para manter o que está gravado.");
            return null;
        }

        try
        {
            string name = SecretNames.For(tenantId, ConnectorSettingsKind.Platform, [platform], "clientSecret");
            writes.Add(new SecretWrite(name, secret, field));
            return SecretReference.Of(name);
        }
        catch (ArgumentException)
        {
            problems.Add($"{label}: a plataforma não serve de nome no cofre (só letras, dígitos e hífen, sem '--', até 127 caracteres).");
            return null;
        }
    }

    // Os conectores de uma conta: só os adapters que a plataforma dela declara, com o nome declarado (D17), e um conector
    // por adapter. As settings passam pela regra dos segredos (escopo de conector, com o adapter no nome), e as referências
    // gravadas voltam só para o mesmo adapter. A credencial da plataforma e a seção por ambiente são recusadas.
    private static IReadOnlyList<PlatformConnector> PrepareConnectors(
        string tenantId, string accountLabel, string platform, IReadOnlyList<PlatformConnectorRequest> requested,
        IReadOnlyList<PlatformConnector> stored, IReadOnlyList<IAdapterDeclaration> declarations, List<SecretWrite> writes,
        List<string> problems)
    {
        string[] adapters = [.. declarations.Where(d => string.Equals(d.Platform, platform, StringComparison.Ordinal)).Select(d => d.Adapter)];
        var connectors = new List<PlatformConnector>();
        foreach (PlatformConnectorRequest connector in requested)
        {
            string requestedAdapter = connector.Adapter?.Trim() ?? string.Empty;
            if (requestedAdapter.Length == 0)
            {
                problems.Add($"{accountLabel}.Connectors: um conector veio sem o adapter.");
                continue;
            }

            string? adapter = adapters.FirstOrDefault(a => string.Equals(a, requestedAdapter, StringComparison.OrdinalIgnoreCase));
            if (adapter is null)
            {
                problems.Add($"{accountLabel}.Connectors[{requestedAdapter}]: a plataforma '{platform}' não declara o adapter "
                    + $"'{requestedAdapter}'. Os declarados são: {Listed(adapters)}.");
                continue;
            }

            string label = $"{accountLabel}.Connectors[{adapter}].Settings";
            IAdapterDeclaration declaration = declarations.First(
                d => string.Equals(d.Platform, platform, StringComparison.Ordinal) && string.Equals(d.Adapter, adapter, StringComparison.Ordinal));
            AddBaseUrlProblems(connector.Settings, label, AdapterDeclarations.VersionsOf(declaration), problems);

            PlatformConnector? storedConnector = stored.FirstOrDefault(c => string.Equals(c.Adapter, adapter, StringComparison.OrdinalIgnoreCase));
            string? storedSettings = storedConnector?.Settings;
            AddPlatformCredentialProblems(connector.Settings, label, problems);
            AddEnvironmentSectionProblems(connector.Settings, label, problems);
            Prepared prepared = Prepare(tenantId, ConnectorSettingsKind.Connector, connector.Settings, storedSettings, problems, label, [adapter]);
            writes.AddRange(prepared.Writes);
            IReadOnlyList<ConnectorModule> modules = PrepareModules(
                $"{accountLabel}.Connectors[{adapter}]", declaration, connector, storedConnector?.Modules ?? [], problems);
            AddRequiredBaseProblems(prepared.Json, label, declaration, modules, problems);
            connectors.Add(new PlatformConnector { Adapter = adapter, Settings = prepared.Json, Modules = modules });
        }

        // Um conector por adapter na conta (D2): a chave do conector é o adapter, e ela entra no nome dos segredos.
        problems.AddRange(connectors.GroupBy(c => c.Adapter, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"{accountLabel}.Connectors: o adapter '{g.Key}' aparece {g.Count()} vezes, e a conta tem um conector por adapter."));

        return connectors;
    }

    // O que o conector liga (6.1): os módulos e as integrações que o adapter declara, com o nome declarado — identidade de um
    // conjunto fechado, como a plataforma e o adapter (D17) —, e cada integração só com o módulo dela ligado. Um módulo ligado
    // sem integração é aceito. Os dois ausentes mantêm o gravado; um só ausente, o gravado dele. O caminho ausente mantém o
    // gravado — inclusive o nulo que espera o host (D10) —, e uma integração nova sem caminho fica inativa (D5).
    private static IReadOnlyList<ConnectorModule> PrepareModules(
        string label, IAdapterDeclaration declaration, PlatformConnectorRequest connector, IReadOnlyList<ConnectorModule> stored,
        List<string> problems)
    {
        if (connector.Modules is null && connector.Integrations is null)
        {
            return stored;
        }

        Dictionary<string, ConnectorIntegration> storedIntegrations = stored.SelectMany(m => m.Integrations)
            .GroupBy(i => i.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        IEnumerable<string> requestedModules = connector.Modules ?? stored.Select(m => m.Module);
        IEnumerable<ConnectorIntegrationRequest> requestedIntegrations = connector.Integrations
            ?? storedIntegrations.Values.Select(i => new ConnectorIntegrationRequest(i.Key, i.Endpoint, i.BatchSize));

        string[] declaredModules = [.. declaration.Modules.Select(m => m.Module)];
        var on = new HashSet<string>(StringComparer.Ordinal);
        foreach (string requested in requestedModules)
        {
            string? module = declaredModules.FirstOrDefault(m => string.Equals(m, requested?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (module is null)
            {
                problems.Add($"{label}.Modules: o adapter '{declaration.Adapter}' não declara o módulo '{requested}'. "
                    + $"Os declarados são: {Listed(declaredModules)}.");
                continue;
            }

            on.Add(module);
        }

        // As integrações declaradas, pela chave: o módulo dela e a ordem da declaração, que é a ordem em que ficam gravadas.
        var declared = new Dictionary<string, (string Key, string Module, int Order, bool AcceptsBatch)>(StringComparer.OrdinalIgnoreCase);
        foreach (DeclaredModule module in declaration.Modules)
        {
            foreach (DeclaredIntegration integration in module.Integrations)
            {
                declared[integration.Key] = (integration.Key, module.Module, declared.Count, integration.AcceptsBatch);
            }
        }

        var integrations = new List<(string Module, int Order, ConnectorIntegration Integration)>();
        foreach (ConnectorIntegrationRequest requested in requestedIntegrations)
        {
            string requestedKey = requested.Key?.Trim() ?? string.Empty;
            if (!declared.TryGetValue(requestedKey, out (string Key, string Module, int Order, bool AcceptsBatch) found))
            {
                problems.Add($"{label}.Integrations[{requestedKey}]: o adapter '{declaration.Adapter}' não declara a integração "
                    + $"'{requestedKey}'. As declaradas são: {Listed([.. declared.Values.Select(d => d.Key)])}.");
                continue;
            }

            if (!on.Contains(found.Module))
            {
                problems.Add($"{label}.Integrations[{found.Key}]: a integração '{found.Key}' é do módulo '{found.Module}', que não está "
                    + "ligado neste conector. Ligue o módulo junto com a integração.");
                continue;
            }

            // O lote é por integração, e só onde a API dela aceita (6.6): o que cabe num lote depende do tamanho do registro.
            if (requested.BatchSize is not null && !found.AcceptsBatch)
            {
                problems.Add($"{label}.Integrations[{found.Key}].batchSize: a API da integração '{found.Key}' não aceita lote, e ela "
                    + "não guarda tamanho de lote.");
            }
            else if (requested.BatchSize is <= 0)
            {
                problems.Add($"{label}.Integrations[{found.Key}].batchSize: o tamanho do lote precisa ser positivo.");
            }

            string? endpoint = requested.Endpoint?.Trim()
                ?? (storedIntegrations.TryGetValue(found.Key, out ConnectorIntegration? before) ? before.Endpoint : string.Empty);
            integrations.Add((found.Module, found.Order,
                new ConnectorIntegration { Key = found.Key, Endpoint = endpoint, BatchSize = requested.BatchSize }));
        }

        problems.AddRange(integrations.GroupBy(i => i.Integration.Key, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"{label}.Integrations: a integração '{g.Key}' aparece {g.Count()} vezes, e o conector a liga uma vez só."));

        return [.. declaration.Modules.Where(m => on.Contains(m.Module)).Select(m => new ConnectorModule
        {
            Module = m.Module,
            Integrations = [.. integrations.Where(i => i.Module == m.Module).OrderBy(i => i.Order).Select(i => i.Integration)],
        })];
    }

    // A versão que uma integração ligada usa precisa da base no mapa (D4, 6.5) — ligada, com caminho ou sem: a base é do
    // conector, e o caminho em branco só deixa a integração inativa. Um módulo ligado sem integração não exige nada.
    private static void AddRequiredBaseProblems(
        string settings, string label, IAdapterDeclaration declaration, IReadOnlyList<ConnectorModule> modules, List<string> problems)
    {
        Dictionary<string, string> versionOf = declaration.Modules.SelectMany(m => m.Integrations)
            .ToDictionary(i => i.Key, i => i.Version, StringComparer.Ordinal);
        IEnumerable<IGrouping<string, string>> needed = modules.SelectMany(m => m.Integrations)
            .Where(i => versionOf.ContainsKey(i.Key))
            .GroupBy(i => versionOf[i.Key], i => i.Key, StringComparer.Ordinal);

        JsonObject? bases = null;
        if (TryParseObject(settings, out JsonObject? root, out _))
        {
            try
            {
                bases = root["baseUrls"] as JsonObject;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Nome repetido: o Prepare já recusa.
            }
        }

        foreach (IGrouping<string, string> version in needed)
        {
            bool present = bases is not null && bases.TryGetPropertyValue(version.Key, out JsonNode? value)
                && value is JsonValue text && text.TryGetValue(out string? url) && !string.IsNullOrWhiteSpace(url);
            if (!present)
            {
                problems.Add($"{label}.baseUrls.{version.Key}: falta a URL base da versão '{version.Key}', que as integrações ligadas "
                    + $"{Listed([.. version])} usam.");
            }
        }
    }

    // O mapa de URLs base contra a declaração do adapter (D4): uma chave por versão declarada — exata, porque a leitura a
    // acha exata —, com a URL na regra de https, ou vazia. A versão declarada que nenhuma integração ligada usa é aceita
    // vazia; a exigida por integração ligada é regra do grupo 6. A chave é rótulo, e não entra na URL.
    private static void AddBaseUrlProblems(string? json, string label, IReadOnlyList<string> versions, List<string> problems)
    {
        if (!TryParseObject(json, out JsonObject? root, out _))
        {
            return;   // o Prepare já recusa
        }

        try
        {
            if (!root.TryGetPropertyValue("baseUrls", out JsonNode? bases) || bases is null)
            {
                return;
            }

            if (bases is not JsonObject map)
            {
                problems.Add($"{label}.baseUrls: o mapa de URLs base precisa ser um objeto, de versão para URL.");
                return;
            }

            foreach ((string version, JsonNode? value) in map)
            {
                string field = $"{label}.baseUrls.{version}";
                if (!versions.Contains(version, StringComparer.Ordinal))
                {
                    problems.Add($"{field}: o adapter não declara a versão '{version}'. As declaradas são: {Listed(versions)}.");
                }
                else if (value is null)
                {
                    continue;   // a declarada que nenhuma integração usa
                }
                else if (value is not JsonValue text || !text.TryGetValue(out string? url))
                {
                    problems.Add($"{field}: a URL base precisa ser texto.");
                }
                else if (!string.IsNullOrWhiteSpace(url) && !ConnectorUrls.TryParse(url.Trim(), out _))
                {
                    problems.Add($"{field}: a URL base {ConnectorUrls.Rule}.");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Nome repetido: o Prepare já recusa.
        }
    }

    // Os nomes de uma lista, para a mensagem; vazia, diz que não há nenhum.
    private static string Listed(IReadOnlyCollection<string> names) => names.Count == 0 ? "nenhum" : string.Join(", ", names);

    // A credencial da plataforma é da conta, e nunca do conector (ADR-0036 §2): o clientId e o clientSecret nas settings de
    // um conector são recusados, em qualquer nível. O clientId não é campo de segredo, e por isso a regra é esta, e não a
    // regra de nomes de segredo; os outros segredos do conector (apiKey…) seguem a regra de sempre.
    private static void AddPlatformCredentialProblems(string? json, string label, List<string> problems)
    {
        if (!TryParseObject(json, out JsonObject? root, out _))
        {
            return;   // o Prepare já recusa
        }

        try
        {
            Walk(root, label);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Nome repetido: o Prepare já recusa.
        }

        void Walk(JsonNode? node, string path)
        {
            switch (node)
            {
                case JsonArray array:
                    for (int i = 0; i < array.Count; i++)
                    {
                        Walk(array[i], $"{path}[{i.ToString(CultureInfo.InvariantCulture)}]");
                    }

                    break;
                case JsonObject container:
                    foreach ((string name, JsonNode? value) in container)
                    {
                        if (ConnectorSecretFields.Normalize(name) is "clientid" or "clientsecret")
                        {
                            problems.Add($"{path}.{name}: a credencial da plataforma é da conta, e não do conector. Grave-a na conta de plataforma.");
                        }
                        else
                        {
                            Walk(value, $"{path}.{name}");
                        }
                    }

                    break;
            }
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // As settings de um conector têm um nível só: o ambiente é da instância, e uma seção "sandbox" ou "production" seria
    // configuração que nada lê — e o segredo dela cairia num nome com o ambiente (change platform-account-and-connectors, D6).
    // Recusada na gravação, e não guardada em silêncio.
    private static void AddEnvironmentSectionProblems(string? json, string label, List<string> problems)
    {
        if (!TryParseObject(json, out JsonObject? root, out _))
        {
            return;   // o Prepare já recusa
        }

        IEnumerable<string> sections;
        try
        {
            sections = [.. root.Select(p => p.Key)
                .Where(k => k.Equals(InstanceEnvironment.Sandbox, StringComparison.OrdinalIgnoreCase)
                    || k.Equals(InstanceEnvironment.Production, StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return;   // nome repetido: o Prepare já recusa
        }

        problems.AddRange(sections.Select(s =>
            $"{label}.{s}: as settings do conector não têm seção por ambiente — o ambiente é da instância "
            + $"({InstanceEnvironment.ConfigurationKey}), e as settings valem para ele. Grave os campos no primeiro nível, e "
            + "recarregue a tela se ela ainda mostra Sandbox e Produção."));
    }

    // Só a referência do próprio tenant é descrita: a de outro tenant, ou malformada, é "não configurado" sem ir ao cofre.
    private async Task<SecretStatus> StatusAsync(string tenantId, string? reference, CancellationToken ct)
    {
        if (!SecretReference.TryParse(reference, out string? name) || !SecretNames.BelongsTo(name, tenantId))
        {
            return new SecretStatus(false, null);
        }

        SecretDescription? description = await _secrets.DescribeAsync(name, ct);
        return description is null ? new SecretStatus(false, null) : new SecretStatus(true, description.UpdatedOn);
    }

    // A seção poll é o contrato do coletor (igual para qualquer origem): o valor que esta gravação escreve e que o poller
    // não leria é recusado aqui, e não vira falha a cada intervalo. O inválido já gravado, que volta igual, passa (D8).
    private static void AddPollProblems(string json, string? storedJson, List<string> problems)
    {
        if (!TryParseObject(json, out JsonObject? written, out _))
        {
            return;   // o Prepare já recusou
        }

        JsonObject? stored = TryParseObject(storedJson, out JsonObject? storedRoot, out _) ? storedRoot : null;
        IReadOnlyList<string> found;
        try
        {
            found = ChangeFeedPollSettings.ProblemsInWrite(written, stored);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Nome repetido nas settings gravadas (o pedido já foi reserializado): não dá para dizer o que volta igual.
            found = ChangeFeedPollSettings.ProblemsInWrite(written, null);
        }

        problems.AddRange(found.Select(p => $"{ConnectorSettingsKind.Inbound}Settings.{p}"));
    }

    // As referências gravadas só são mantidas para o mesmo adapter: as de outro adapter são de outro schema.
    private static string? StoredFor(string? storedAdapter, string? adapter, string? storedSettings)
        => storedAdapter is not null && string.Equals(storedAdapter, adapter, StringComparison.OrdinalIgnoreCase) ? storedSettings : null;

    // ---- Gravação: validar, converter o campo de escrita em referência, manter a referência do campo ausente ----

    private static Prepared Prepare(
        string tenantId, ConnectorSettingsKind kind, string? json, string? storedJson, List<string> problems,
        string? display = null, IReadOnlyList<string>? pathPrefix = null)
    {
        string label = display ?? $"{kind}Settings";
        if (!TryParseObject(json, out JsonObject? root, out string? invalid))
        {
            problems.Add($"{label} {invalid}");
            return new Prepared("{}", []);
        }

        var writes = new List<SecretWrite>();
        try
        {
            ConvertWriteFields(root, [.. pathPrefix ?? []], label, tenantId, kind, writes, problems);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Nome de campo repetido no mesmo objeto só aparece quando o JSON é percorrido.
            problems.Add($"{label} não é um JSON válido.");
            return new Prepared("{}", []);
        }

        if (TryParseObject(storedJson, out JsonObject? storedRoot, out _))
        {
            KeepStoredReferences(storedRoot, root);
        }

        return new Prepared(root.ToJsonString(Compact), writes);
    }

    private static void ConvertWriteFields(
        JsonNode? node, List<string> path, string display, string tenantId, ConnectorSettingsKind kind,
        List<SecretWrite> writes, List<string> problems)
    {
        if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                string index = i.ToString(CultureInfo.InvariantCulture);
                ConvertWriteFields(array[i], [.. path, index], $"{display}[{index}]", tenantId, kind, writes, problems);
            }

            return;
        }

        if (node is not JsonObject container)
        {
            return;
        }

        foreach ((string name, JsonNode? value) in container.ToList())
        {
            string field = $"{display}.{name}";
            if (ConnectorSecretFields.IsReferenceField(name))
            {
                problems.Add($"{field}: a referência de segredo não vem na requisição; mande o valor no campo de segredo, e o servidor grava a referência.");
                continue;
            }

            if (!ConnectorSecretFields.IsWriteField(name))
            {
                ConvertWriteFields(value, [.. path, name], field, tenantId, kind, writes, problems);
                continue;
            }

            // O campo de escrita nunca é persistido: sai sempre, e o valor, se houver, vira referência.
            container.Remove(name);
            if (value is null)
            {
                continue;
            }

            if (value is not JsonValue text || !text.TryGetValue(out string? secret))
            {
                problems.Add($"{field} é campo de segredo e precisa ser texto.");
                continue;
            }

            if (secret.Length == 0)
            {
                continue;   // vazio conta como ausente: mantém o que já estava configurado
            }

            // A máscara é placeholder da tela, e nunca valor: gravá-la destruiria o segredo no cofre (D3).
            if (ConnectorSecretFields.IsMask(secret))
            {
                problems.Add($"{field} veio com a máscara da tela, e não com o segredo. Digite o segredo, ou deixe o campo vazio para manter o que está gravado.");
                continue;
            }

            string secretName;
            try
            {
                secretName = SecretNames.For(tenantId, kind, path, ConnectorSecretFields.Normalize(name));
            }
            catch (ArgumentException)
            {
                problems.Add($"{field}: o caminho não serve de nome no cofre (só letras, dígitos e hífen, sem '--', até 127 caracteres).");
                continue;
            }

            container[ConnectorSecretFields.ReferenceOf(name)] = SecretReference.Of(secretName);
            writes.Add(new SecretWrite(secretName, secret, field));
        }
    }

    // Cada referência gravada volta para o mesmo caminho, se o objeto dela ainda existe e não recebeu valor novo.
    private static void KeepStoredReferences(JsonObject storedRoot, JsonObject root)
    {
        var references = new List<(List<string> Path, string Field, JsonNode? Value)>();
        CollectReferences(storedRoot, [], references);

        foreach ((List<string> path, string field, JsonNode? value) in references)
        {
            if (Navigate(root, path) is JsonObject container
                && !container.Any(p => ConnectorSecretFields.Normalize(p.Key) == ConnectorSecretFields.Normalize(field)))
            {
                container[field] = value?.DeepClone();
            }
        }
    }

    private static void CollectReferences(JsonNode? node, List<string> path, List<(List<string>, string, JsonNode?)> references)
    {
        switch (node)
        {
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    CollectReferences(array[i], [.. path, i.ToString(CultureInfo.InvariantCulture)], references);
                }

                break;
            case JsonObject container:
                foreach ((string name, JsonNode? value) in container)
                {
                    if (ConnectorSecretFields.IsReferenceField(name))
                    {
                        references.Add((path, name, value));
                    }
                    else
                    {
                        CollectReferences(value, [.. path, name], references);
                    }
                }

                break;
        }
    }

    private static JsonNode? Navigate(JsonNode root, List<string> path)
    {
        JsonNode? current = root;
        foreach (string step in path)
        {
            current = current switch
            {
                JsonObject container => container.TryGetPropertyValue(step, out JsonNode? next) ? next : null,
                JsonArray array when int.TryParse(step, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i < array.Count => array[i],
                _ => null,
            };
        }

        return current;
    }

    // ---- Leitura: sem referência e sem campo de escrita, com o caminho de cada segredo ----

    private static string Mask(string kind, string? json, List<(string Key, string? Value)> references)
    {
        if (!TryParseObject(json, out JsonObject? root, out _))
        {
            return "{}";   // o que não é JSON não é devolvido: poderia ter um segredo em claro dentro
        }

        try
        {
            Strip(root, kind, references);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "{}";
        }

        return root.ToJsonString(Compact);
    }

    private static void Strip(JsonNode? node, string key, List<(string, string?)> references)
    {
        switch (node)
        {
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    Strip(array[i], $"{key}.{i.ToString(CultureInfo.InvariantCulture)}", references);
                }

                break;
            case JsonObject container:
                foreach ((string name, JsonNode? value) in container.ToList())
                {
                    if (ConnectorSecretFields.IsReferenceField(name))
                    {
                        container.Remove(name);
                        references.Add(($"{key}.{ConnectorSecretFields.WriteFieldOf(name)}",
                            value is JsonValue text && text.TryGetValue(out string? reference) ? reference : null));
                    }
                    else if (ConnectorSecretFields.IsWriteField(name))
                    {
                        container.Remove(name);   // valor em claro que chegou por outro caminho (SQL, seed): nunca sai
                    }
                    else
                    {
                        Strip(value, $"{key}.{name}", references);
                    }
                }

                break;
        }
    }

    private static bool TryParseObject(string? json, [NotNullWhen(true)] out JsonObject? root, [NotNullWhen(false)] out string? problem)
    {
        root = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            root = new JsonObject();
            return true;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            problem = "não é um JSON válido.";   // sem o texto da exceção: ele pode citar o conteúdo
            return false;
        }

        if (node is not JsonObject obj)
        {
            problem = "precisa ser um objeto JSON.";
            return false;
        }

        root = obj;
        return true;
    }

    private sealed record Prepared(string Json, IReadOnlyList<SecretWrite> Writes);

    // Carrega o valor: classe, e não record, para que nenhum ToString o imprima.
    private sealed class SecretWrite(string name, string value, string field)
    {
        public string Name { get; } = name;

        public string Value { get; } = value;

        public string Field { get; } = field;
    }
}
