using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Assina a dead-letter da fila e registra cada mensagem esgotada como documento não-processável.
/// Sem isso, uma nota que falha repetidamente sumiria de vista; aqui ela vira um item rastreável
/// (ADR-0010). Não reprocessa — só torna a falha visível.
/// </summary>
internal sealed class DeadLetterTriggerService : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IServiceProvider _services;
    private readonly ILogger<DeadLetterTriggerService> _logger;
    private readonly string _queueName;
    private ServiceBusProcessor? _processor;

    public DeadLetterTriggerService(
        ServiceBusClient client,
        IServiceProvider services,
        IOptions<ServiceBusOptions> options,
        ILogger<DeadLetterTriggerService> logger)
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
            SubQueue = SubQueue.DeadLetter,
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
        DeadLetterHandler handler = scope.ServiceProvider.GetRequiredService<DeadLetterHandler>();

        string reason = args.Message.DeadLetterReason
            ?? args.Message.DeadLetterErrorDescription
            ?? "Mensagem movida para dead-letter.";

        await handler.HandleAsync(args.Message.Body, reason, args.CancellationToken);
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Erro no processamento da dead-letter {Entity}.", args.EntityPath);
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
