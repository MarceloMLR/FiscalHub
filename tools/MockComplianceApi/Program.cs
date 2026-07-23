using System.Collections.Concurrent;

// Mock minimal-API que simula a plataforma de compliance (Avalara) para testes manuais e2e.
// Fluxo de duas fases (ADR-0003): POST devolve um GUID (aceito); GET status devolve o resultado.
// Store em memória — some quando o processo reinicia. Nunca usar em produção.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// guid -> (status nativo, corpo JSON recebido)
var documents = new ConcurrentDictionary<string, (string Status, string Body)>();

// Resultado padrão aplicado a novos documentos quando o envio não traz ?resultado. O host não passa
// query, então este toggle é o que decide carregado/erro — permite forçar o caminho de rejeição.
var toggle = new ResultToggle();

// Fase 1 — recebe o "god json" e devolve um identificador externo (GUID).
// O resultado da consulta é controlável via ?resultado=carregado|erro para exercitar os dois ramos.
app.MapPost("/documents", async (HttpRequest request, string? resultado) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var status = resultado?.Trim().ToLowerInvariant() switch
    {
        "erro" => "erro",
        "carregado" => "carregado",
        _ => toggle.Value, // sem query explícita: usa o toggle (padrão 'carregado')
    };

    var id = Guid.NewGuid().ToString();
    documents[id] = (status, body);
    return Results.Ok(new { id });
});

// Fase 2 — consulta o status final pelo GUID.
app.MapGet("/documents/{id}/status", (string id) =>
    documents.TryGetValue(id, out var doc)
        ? Results.Ok(new { id, status = doc.Status })
        : Results.NotFound(new { id, status = "desconhecido" }));

// Inspeção: devolve o JSON exato que o hub enviou (para ver o payload gerado).
app.MapGet("/documents/{id}", (string id) =>
    documents.TryGetValue(id, out var doc)
        ? Results.Content(doc.Body, "application/json")
        : Results.NotFound());

// Toggle (dev): força o resultado padrão dos próximos documentos, pra exercitar erro/reprocesso ao vivo.
app.MapPost("/admin/result/{value}", (string value) =>
{
    toggle.Value = value.Trim().Equals("erro", StringComparison.OrdinalIgnoreCase) ? "erro" : "carregado";
    return Results.Ok(new { defaultResult = toggle.Value });
});

app.MapGet("/admin/result", () => Results.Ok(new { defaultResult = toggle.Value }));

app.Run();

// Estado do toggle de resultado (dev). Só o mock usa; não vai pra produção.
internal sealed class ResultToggle
{
    public string Value { get; set; } = "carregado";
}
