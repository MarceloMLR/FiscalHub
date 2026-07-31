using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiscalHub.Application.Support;

namespace FiscalHub.Adapters.Support;

/// <summary>
/// Adapter real do Freshdesk: abre o chamado via <c>POST /api/v2/tickets</c> em multipart/form-data
/// (pra levar os anexos), autenticado por Basic auth com a API key (<c>apiKey:X</c>). Domínio e
/// credenciais vêm das settings do perfil do tenant (segredo por referência em produção).
/// </summary>
internal sealed class FreshdeskSupportGateway : ISupportTicketGateway
{
    private readonly IHttpClientFactory _httpFactory;

    public FreshdeskSupportGateway(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public string Name => "Freshdesk";

    public async Task<TicketResult> OpenAsync(SupportTicket ticket, string settingsJson, CancellationToken ct = default)
    {
        FreshdeskSettings settings = Parse(settingsJson);
        string host = Normalize(settings.Domain);
        string apiKey = settings.ApiKey
            ?? throw new SupportTicketException("Freshdesk: 'apiKey' ausente nas settings do conector.");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(ticket.Subject), "subject" },
            { new StringContent(ToHtml(ticket.DescriptionText)), "description" },
            { new StringContent(settings.RequesterEmail ?? "no-reply@fiscalhub.local"), "email" },
            { new StringContent(settings.Priority.ToString()), "priority" },
            { new StringContent(settings.Status.ToString()), "status" },
        };

        // Anexos: um zip por nota. Freshdesk espera o campo repetido "attachments[]".
        foreach (TicketAttachment att in ticket.Attachments)
        {
            var file = new ByteArrayContent(att.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(att.ContentType);
            form.Add(file, "attachments[]", att.FileName);
        }

        HttpClient http = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/api/v2/tickets") { Content = form };
        // Basic auth = base64("{apiKey}:X"); a senha é ignorada pelo Freshdesk quando se usa API key.
        string basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        HttpResponseMessage response = await http.SendAsync(request, ct);
        string payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new SupportTicketException($"Freshdesk recusou o chamado ({(int)response.StatusCode}): {payload}");
        }

        long id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetInt64();
        return new TicketResult(id.ToString(), $"https://{host}/a/tickets/{id}");
    }

    private static FreshdeskSettings Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FreshdeskSettings>(json, JsonOpts) ?? new FreshdeskSettings();
        }
        catch (JsonException)
        {
            return new FreshdeskSettings();
        }
    }

    // Aceita "empresa.freshdesk.com", "https://empresa.freshdesk.com/" → devolve só o host.
    private static string Normalize(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new SupportTicketException("Freshdesk: 'domain' ausente nas settings do conector.");
        }
        string d = domain.Trim().Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/');
        return d;
    }

    // A descrição do Freshdesk é HTML; preserva a formatação do texto num bloco pré-formatado.
    private static string ToHtml(string text)
    {
        string escaped = text
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        return $"<pre style=\"font-family:inherit;white-space:pre-wrap\">{escaped}</pre>";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed record FreshdeskSettings
    {
        public string? Domain { get; init; }
        public string? ApiKey { get; init; }
        public string? ApiKeyRef { get; init; }   // produção: referência kv: (resolvida upstream)
        public string? RequesterEmail { get; init; }
        public int Priority { get; init; } = 2;    // 1 Low · 2 Medium · 3 High · 4 Urgent
        public int Status { get; init; } = 2;      // 2 Open · 3 Pending · 4 Resolved · 5 Closed
    }
}
