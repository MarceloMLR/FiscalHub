using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Serialização compartilhada entre quem enfileira e quem consome: camelCase e enum como texto,
/// para a mensagem ser legível e o round-trip bater dos dois lados.
/// </summary>
internal static class DocumentQueueSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
