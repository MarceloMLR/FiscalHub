namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>Configuração da fila de descoberta: para onde o worker de feed de mudanças publica as referências.</summary>
public sealed class ServiceBusDiscoveryQueueOptions
{
    /// <summary>Nome da fila de descoberta.</summary>
    public string QueueName { get; set; } = "documents-discovered";
}
