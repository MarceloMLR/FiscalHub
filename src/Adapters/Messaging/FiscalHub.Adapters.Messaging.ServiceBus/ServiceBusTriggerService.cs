using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Casca do gatilho: assina a fila e, por mensagem, abre um escopo e chama o consumidor. Sucesso =
/// completa a mensagem; exceção = abandona → o Service Bus reconta e, no limite (MaxDeliveryCount),
/// move pra dead-letter. Retry e DLQ são nativos do transporte (ADR-0004).
/// </summary>
internal sealed class ServiceBusTriggerService : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IServiceProvider _services;
    private readonly ILogger<ServiceBusTriggerService> _logger;
    private readonly string _queueName;
    private ServiceBusProcessor? _processor;

    public ServiceBusTriggerService(
        ServiceBusClient client,
        IServiceProvider services,
        IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusTriggerService> logger)
    {
        _client = client;
        _services = services;
        _logger = logger;
        _queueName = options.Value.QueueName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(_queueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        QueuedDocumentProcessor processor = scope.ServiceProvider.GetRequiredService<QueuedDocumentProcessor>();

        await processor.HandleAsync(args.Message.Body, args.Message.CorrelationId, args.CancellationToken);
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Erro no processamento da fila {Entity}.", args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
