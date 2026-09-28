using System.Text.Json;
using FiscalHub.Application.Support;

namespace FiscalHub.Application.Tracing;

/// <summary>
/// A leitura do desfecho de um documento: o que a primeira vista do detalhe usa, tirado das fotos da resposta no
/// servidor, e nada mais delas (design D8 revisado da change establishment-and-readable-dashboard). É aberta a qualquer
/// papel do tenant, enquanto as fotos cruas (o <c>/trace</c> e o zip) exigem o papel que as vê.
/// <list type="bullet">
///   <item><b>Fields:</b> a lista de campos da recusa, do mapa de erros por campo do ProblemDetails (RFC 9110) — um
///   formato padrão, e não o contrato de um destino. Sai da foto da consulta de status quando ela tem o mapa, e senão da
///   do envio.</item>
///   <item><b>Omissions:</b> o que o hub declarou que o contrato não levou, no <c>request.omissions</c> da foto do
///   envio (ADR-0027 §8, revisado pelo ADR-0030).</item>
/// </list>
/// </summary>
public sealed record DocumentReading
{
    // A convenção de nome das fotos da resposta é a do TracePaths (Infrastructure): {destino}.response.{troca}.json.
    private static readonly string SubmitSuffix = $".response.{TraceExchanges.Submit}.json";
    private static readonly string StatusSuffix = $".response.{TraceExchanges.Status}.json";

    public required IReadOnlyList<FieldRejection> Fields { get; init; }

    public required IReadOnlyList<string> Omissions { get; init; }

    /// <summary>A leitura das fotos de um documento. A foto repetida (reprocesso em outro mês) que vem por último prevalece.</summary>
    public static DocumentReading From(IReadOnlyList<TraceFile> files)
    {
        using JsonDocument? submit = LastEnvelope(files, SubmitSuffix);
        using JsonDocument? status = LastEnvelope(files, StatusSuffix);

        IReadOnlyList<FieldRejection>? fields = FieldsOf(status) ?? FieldsOf(submit);
        return new DocumentReading { Fields = fields ?? [], Omissions = OmissionsOf(submit) };
    }

    private static JsonDocument? LastEnvelope(IReadOnlyList<TraceFile> files, string suffix)
    {
        TraceFile? file = files.LastOrDefault(f => f.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(file.Content);
        }
        catch (JsonException)
        {
            return null;   // foto que não é JSON não tem o que ler
        }
    }

    private static IReadOnlyList<FieldRejection>? FieldsOf(JsonDocument? envelope)
    {
        if (envelope is null
            || !TryObject(envelope.RootElement, "response", out JsonElement response)
            || !TryObject(response, "body", out JsonElement body)
            || !TryObject(body, "errors", out JsonElement errors))
        {
            return null;
        }

        List<FieldRejection> fields = [.. errors.EnumerateObject().Select(p => new FieldRejection(p.Name, MessagesOf(p.Value)))];
        return fields.Count > 0 ? fields : null;
    }

    private static IReadOnlyList<string> MessagesOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => [.. value.EnumerateArray().Select(Text)],
        _ => [Text(value)],
    };

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static IReadOnlyList<string> OmissionsOf(JsonDocument? submit)
        => submit is not null
           && TryObject(submit.RootElement, "request", out JsonElement request)
           && request.TryGetProperty("omissions", out JsonElement omissions)
           && omissions.ValueKind == JsonValueKind.Array
            ? [.. omissions.EnumerateArray().Select(Text)]
            : [];

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }
}

/// <summary>Um campo que a plataforma recusou: o caminho como ela o escreveu e as mensagens dela, como vieram.</summary>
public sealed record FieldRejection(string Path, IReadOnlyList<string> Messages);

/// <summary>
/// A leitura do desfecho para quem está logado, com a regra de tenant do <see cref="DocumentTraceQuery"/> (ADR-0028):
/// o documento de outro tenant, ou sem fotos, dá <c>null</c>, e o armazenamento do outro tenant nem é lido.
/// </summary>
public sealed class DocumentReadingQuery
{
    private readonly DocumentTraceQuery _traces;

    public DocumentReadingQuery(DocumentTraceQuery traces) => _traces = traces;

    public async Task<DocumentReading?> GetAsync(string tenantId, string naturalKey, CancellationToken ct = default)
        => await _traces.GetAsync(tenantId, naturalKey, ct) is { } files ? DocumentReading.From(files) : null;
}
