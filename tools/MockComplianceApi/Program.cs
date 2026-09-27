using System.Collections.Concurrent;

// Mock minimal-API que simula a plataforma de compliance (Avalara) para testes manuais e2e.
// Fluxo de duas fases (ADR-0003): POST devolve um GUID (aceito); GET status devolve o resultado.
// Store em memória — some quando o processo reinicia. Nunca usar em produção.
// Os formatos de recusa ({"mensagens":[...]}) são PRESUMIDOS até haver resposta real gravada (ADR-0026); o hub
// extrai o motivo de forma tolerante e não depende deles.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// guid -> (status nativo, motivo da recusa, corpo JSON recebido)
var documents = new ConcurrentDictionary<string, (string Status, string? Reason, string Body)>();

// Resultado padrão aplicado a novos documentos quando o envio não traz ?resultado. O host não passa
// query, então este toggle é o que decide — permite forçar os caminhos de recusa ao vivo.
var toggle = new ResultToggle();

// Fase 1 — recebe o "god json" e devolve um identificador externo (GUID).
// ?resultado=carregado|erro|rejeitar sobrepõe o toggle para exercitar cada ramo.
app.MapPost("/documents", async (HttpRequest request, string? resultado) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var result = ResultToggle.Normalize(resultado) ?? toggle.Value;

    // Recusa síncrona: a plataforma não aceita o documento (o hub registra o motivo, sem retentativa).
    if (result == "rejeitar")
    {
        return Results.BadRequest(new { mensagens = new[] { toggle.Reason } });
    }

    var id = Guid.NewGuid().ToString();
    documents[id] = (result, result == "erro" ? toggle.Reason : null, body);
    return Results.Ok(new { id });
});

// Fase 2 — consulta o status final pelo GUID. No "erro", o motivo vem em "mensagens".
app.MapGet("/documents/{id}/status", (string id) =>
    documents.TryGetValue(id, out var doc)
        ? doc.Reason is null
            ? Results.Ok(new { id, status = doc.Status })
            : Results.Ok(new { id, status = doc.Status, mensagens = new[] { doc.Reason } })
        : Results.NotFound(new { id, status = "desconhecido" }));

// Inspeção: devolve o JSON exato que o hub enviou (para ver o payload gerado).
app.MapGet("/documents/{id}", (string id) =>
    documents.TryGetValue(id, out var doc)
        ? Results.Content(doc.Body, "application/json")
        : Results.NotFound());

// Toggle (dev): força o resultado padrão dos próximos documentos (carregado | erro | rejeitar), com motivo
// opcional (?motivo=...), pra exercitar erro, recusa e reprocesso ao vivo.
app.MapPost("/admin/result/{value}", (string value, string? motivo) =>
{
    toggle.Value = ResultToggle.Normalize(value) ?? "carregado";
    toggle.Reason = string.IsNullOrWhiteSpace(motivo) ? ResultToggle.DefaultReason : motivo;
    return Results.Ok(new { defaultResult = toggle.Value, reason = toggle.Reason });
});

app.MapGet("/admin/result", () => Results.Ok(new { defaultResult = toggle.Value, reason = toggle.Reason }));

app.Run();

// Estado do toggle de resultado (dev). Só o mock usa; não vai pra produção.
internal sealed class ResultToggle
{
    public const string DefaultReason = "Documento recusado pelo mock (simulação de recusa da plataforma).";

    public string Value { get; set; } = "carregado";

    public string Reason { get; set; } = DefaultReason;

    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "carregado" => "carregado",
        "erro" => "erro",
        "rejeitar" => "rejeitar",
        _ => null,
    };
}

// Exposto para o teste ponta a ponta subir o mock em memória (WebApplicationFactory<Program>).
public partial class Program;
