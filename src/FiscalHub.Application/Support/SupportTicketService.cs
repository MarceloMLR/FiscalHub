using System.IO.Compression;
using System.Text;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Queries;

namespace FiscalHub.Application.Support;

/// <summary>
/// Abre um chamado a partir de uma ou mais notas: para cada nota, lê os arquivos de rastreabilidade e
/// os empacota num zip (um por nota — cabe melhor no teto de 20MB do Freshdesk); compõe uma descrição
/// com a mensagem do usuário + um bloco por nota (número, status, motivo, external id, atualização); e
/// entrega ao provider configurado no perfil do tenant. Tudo escopado ao tenant.
/// </summary>
public sealed class SupportTicketService : ISupportTicketService
{
    // Margem de segurança abaixo do teto de 20MB do Freshdesk (headers/campos do multipart + folga).
    private const long MaxAttachmentsBytes = 18L * 1024 * 1024;

    private readonly IConnectorProfileStore _profiles;
    private readonly IDocumentQueries _queries;
    private readonly INoteTraceReader _traces;
    private readonly IReadOnlyList<ISupportTicketGateway> _gateways;

    public SupportTicketService(
        IConnectorProfileStore profiles,
        IDocumentQueries queries,
        INoteTraceReader traces,
        IEnumerable<ISupportTicketGateway> gateways)
    {
        _profiles = profiles;
        _queries = queries;
        _traces = traces;
        _gateways = gateways.ToList();
    }

    public async Task<TicketResult> OpenAsync(
        string tenantId,
        IReadOnlyList<string> naturalKeys,
        string subject,
        string description,
        CancellationToken ct = default)
    {
        if (naturalKeys is null || naturalKeys.Count == 0)
        {
            throw new SupportTicketException("Selecione ao menos uma nota para abrir o chamado.");
        }
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new SupportTicketException("O título do chamado é obrigatório.");
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new SupportTicketException("A descrição do chamado é obrigatória.");
        }

        TenantConnectorProfile? profile = await _profiles.GetAsync(tenantId, ct);
        if (profile is null || string.IsNullOrWhiteSpace(profile.SupportAdapter))
        {
            throw new SupportTicketException("Abertura de chamado não está configurada para este tenant.");
        }

        ISupportTicketGateway? gateway = _gateways.FirstOrDefault(
            g => g.Name.Equals(profile.SupportAdapter, StringComparison.OrdinalIgnoreCase));
        if (gateway is null)
        {
            throw new SupportTicketException($"Adapter de chamados '{profile.SupportAdapter}' não está registrado.");
        }

        // Só as notas deste tenant entram (o store filtra por tenant); chaves de outro tenant somem.
        IReadOnlyList<DocumentSummary> notes = await _queries.ListByKeysAsync(tenantId, naturalKeys, ct);
        if (notes.Count == 0)
        {
            throw new SupportTicketException("Nenhuma das notas selecionadas foi encontrada neste tenant.");
        }

        var attachments = new List<TicketAttachment>();
        long total = 0;
        foreach (DocumentSummary note in notes)
        {
            byte[]? zip = await BuildNoteZipAsync(note.TenantId, note.NaturalKey, ct);
            if (zip is null)
            {
                continue;   // nota sem arquivos de rastreabilidade — segue sem anexo
            }
            if (total + zip.Length > MaxAttachmentsBytes)
            {
                throw new SupportTicketException(
                    "Os logs das notas selecionadas passam do limite de 20MB do chamado. Selecione menos notas.");
            }
            total += zip.Length;
            attachments.Add(new TicketAttachment(SafeName(note.NaturalKey) + ".zip", zip));
        }

        string body = ComposeDescription(description, notes);
        var ticket = new SupportTicket
        {
            TenantId = tenantId,
            Subject = subject.Trim(),
            DescriptionText = body,
            Attachments = attachments,
        };

        return await gateway.OpenAsync(ticket, profile.SupportSettings ?? "{}", ct);
    }

    private async Task<byte[]?> BuildNoteZipAsync(string tenantId, string naturalKey, CancellationToken ct)
    {
        IReadOnlyList<TraceFile> files = await _traces.ReadAsync(tenantId, naturalKey, ct);
        if (files.Count == 0)
        {
            return null;
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (TraceFile file in files)
            {
                ZipArchiveEntry entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
                await using Stream stream = entry.Open();
                await stream.WriteAsync(file.Content, ct);
            }
        }
        return ms.ToArray();
    }

    private static string ComposeDescription(string userText, IReadOnlyList<DocumentSummary> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine(userText.Trim());
        sb.AppendLine();
        sb.AppendLine($"— Notas ({notes.Count}) —");
        foreach (DocumentSummary n in notes)
        {
            sb.AppendLine();
            sb.AppendLine($"Nota: {n.Number ?? n.NaturalKey}");
            sb.AppendLine($"  Chave: {n.NaturalKey}");
            sb.AppendLine($"  Status: {n.Status}");
            if (!string.IsNullOrWhiteSpace(n.Reason))
            {
                sb.AppendLine($"  Motivo: {n.Reason}");
            }
            if (!string.IsNullOrWhiteSpace(n.ExternalId))
            {
                sb.AppendLine($"  ID externo: {n.ExternalId}");
            }
            sb.AppendLine($"  Tentativas: {n.Attempts}");
            sb.AppendLine($"  Atualizado: {n.UpdatedAt:yyyy-MM-dd HH:mm} UTC");
        }
        sb.AppendLine();
        sb.AppendLine("Os logs de cada nota (origem/domínio/destino) seguem anexados, zipados por nota.");
        return sb.ToString();
    }

    // Nome de arquivo seguro pro anexo (a chave pode ter caracteres de caminho).
    private static string SafeName(string key)
    {
        Span<char> buffer = stackalloc char[key.Length];
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            buffer[i] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }
        return new string(buffer);
    }
}
