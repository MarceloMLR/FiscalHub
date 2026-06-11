using System.Collections.Concurrent;

// Mock minimal-API que simula a plataforma de compliance (Avalara) para testes manuais e2e.
// Fluxo de duas fases (ADR-0003): POST devolve um GUID (aceito); GET status devolve o resultado.
// Store em memória — some quando o processo reinicia. Nunca usar em produção.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// guid -> status nativo ("carregado" | "erro" | "processando")
var documents = new ConcurrentDictionary<string, string>();

// Fase 1 — recebe o "god json" e devolve um identificador externo (GUID).
// O resultado da consulta é controlável via ?resultado=carregado|erro para exercitar os dois ramos.
app.MapPost("/documents", async (HttpRequest request, string? resultado) =>
{
    using var reader = new StreamReader(request.Body);
    _ = await reader.ReadToEndAsync(); // corpo aceito como-está; o mock não valida o conteúdo.

    var status = resultado?.Trim().ToLowerInvariant() switch
    {
        "erro" => "erro",
        "carregado" => "carregado",
        _ => "carregado", // default: aceito e carregado
    };

    var id = Guid.NewGuid().ToString();
    documents[id] = status;
    return Results.Ok(new { id });
});

// Fase 2 — consulta o status final pelo GUID.
app.MapGet("/documents/{id}/status", (string id) =>
    documents.TryGetValue(id, out var status)
        ? Results.Ok(new { id, status })
        : Results.NotFound(new { id, status = "desconhecido" }));

app.Run();
