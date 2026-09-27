using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Serialização do contrato da Avalara: camelCase, e campo nulo não é escrito — o que o documento não tem fica fora do
/// payload, em vez de virar <c>null</c> explícito que a plataforma poderia ler como zero (ADR-0026).
/// </summary>
internal static class AvalaraJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
