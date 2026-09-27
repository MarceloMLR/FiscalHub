using System.Text.Json;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// As respostas cruas que compõem um documento do D365: o cabeçalho, as linhas, os impostos (de linha e de
/// encargo), os encargos e as linhas contábeis casadas no complemento (vazio quando não houve complemento).
/// É a entrada do canônico (hash e foto da fonte) e da montagem.
/// </summary>
internal sealed record D365DocumentRows(
    JsonElement Header,
    IReadOnlyList<JsonElement> Lines,
    IReadOnlyList<JsonElement> Taxes,
    IReadOnlyList<JsonElement> Charges,
    IReadOnlyList<JsonElement> Accounting);
