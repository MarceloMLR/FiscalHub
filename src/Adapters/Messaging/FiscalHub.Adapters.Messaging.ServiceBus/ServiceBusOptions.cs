namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>Configuração do adapter de fila: connection string e nome da fila de entrada.</summary>
public sealed class ServiceBusOptions
{
    /// <summary>Connection string do Service Bus (ou do emulador, com <c>UseDevelopmentEmulator=true</c>).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Nome da fila de entrada.</summary>
    public string QueueName { get; set; } = "documents-in";
}
