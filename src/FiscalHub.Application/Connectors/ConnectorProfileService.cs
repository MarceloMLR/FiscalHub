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
/// <para>Ordem da gravação: valida as três settings antes de escrever qualquer coisa; grava os segredos no cofre;
/// grava o perfil; e avisa os observadores sempre que o cofre ou o perfil mudou — mesmo que o perfil falhe depois de
/// uma escrita no cofre, porque o nome é fixo por caminho e o segredo já mudou.</para>
/// </summary>
public sealed class ConnectorProfileService
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IConnectorProfileStore _profiles;
    private readonly ISecretStore _secrets;
    private readonly IEnumerable<IConnectorProfileObserver> _observers;
    private readonly ITenantContext _tenant;

    public ConnectorProfileService(
        IConnectorProfileStore profiles,
        ISecretStore secrets,
        IEnumerable<IConnectorProfileObserver> observers,
        ITenantContext tenant)
    {
        _profiles = profiles;
        _secrets = secrets;
        _observers = observers;
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
        Prepared outbound = Prepare(tenantId, ConnectorSettingsKind.Outbound, request.OutboundSettings,
            StoredFor(stored?.OutboundAdapter, request.OutboundAdapter, stored?.OutboundSettings), problems);
        Prepared support = request.SupportSettings is null
            ? new Prepared(stored?.SupportSettings ?? "{}", [])
            : Prepare(tenantId, ConnectorSettingsKind.Support, request.SupportSettings,
                StoredFor(stored?.SupportAdapter, supportAdapter, stored?.SupportSettings), problems);

        // Módulos ausentes mantêm os gravados, como o segredo ausente mantém a referência (D2).
        IReadOnlyList<string>? modules = stored?.Modules;
        if (request.Modules is not null)
        {
            if (TenantModules.TryNormalize(request.Modules, out IReadOnlyList<string> normalized, out string? moduleProblem))
            {
                modules = normalized;
            }
            else
            {
                problems.Add(moduleProblem);
            }
        }

        SecretWrite[] writes = [.. inbound.Writes, .. outbound.Writes, .. support.Writes];
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
                Environment = request.Environment,
                InboundAdapter = request.InboundAdapter,
                InboundSettings = inbound.Json,
                OutboundAdapter = request.OutboundAdapter,
                OutboundSettings = outbound.Json,
                SupportAdapter = supportAdapter,
                SupportSettings = support.Json,
                Modules = modules,
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
        string outbound = Mask("outbound", profile.OutboundSettings, references);
        string support = Mask("support", profile.SupportSettings, references);

        var secrets = new Dictionary<string, SecretStatus>(StringComparer.Ordinal);
        foreach ((string key, string? value) in references)
        {
            secrets[key] = await StatusAsync(tenantId, value, ct);
        }

        return new ConnectorProfileView(profile.TenantId, profile.Environment,
            profile.InboundAdapter, inbound, profile.OutboundAdapter, outbound, profile.SupportAdapter, support, secrets,
            TenantModules.Of(profile));
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

    private static Prepared Prepare(string tenantId, ConnectorSettingsKind kind, string? json, string? storedJson, List<string> problems)
    {
        string label = $"{kind}Settings";
        if (!TryParseObject(json, out JsonObject? root, out string? invalid))
        {
            problems.Add($"{label} {invalid}");
            return new Prepared("{}", []);
        }

        var writes = new List<SecretWrite>();
        try
        {
            ConvertWriteFields(root, [], label, tenantId, kind, writes, problems);
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
