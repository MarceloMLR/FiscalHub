using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Assina a dead-letter da fila e registra cada mensagem esgotada como documento não-processável.
/// Sem isso, uma nota que falha repetidamente sumiria de vista; aqui ela vira um item rastreável
/// (ADR-0010). Não reprocessa — só torna a falha visível.
/// <para>
/// **Nenhuma decisão mora aqui.** Esta casca lê os fatos da mensagem, chama o handler e conclui; ela não escolhe o
/// motivo nem o desfecho. É deliberado: ela é o único trecho do caminho sem teste unitário — <c>OnMessageAsync</c>
/// recebe um <c>ProcessMessageEventArgs</c> com um receiver vivo, e o repositório não tem biblioteca de mock —, e
/// decisão que mora em código sem teste é decisão que ninguém prova. Foi assim que o laço de reentrega e o motivo
/// vazio sobreviveram aqui (design, D1 e D2).
/// </para>
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
        string queueName,
        ILogger<DeadLetterTriggerService> logger)
    {
        _client = client;
        _services = services;
        _logger = logger;
        _queueName = queueName;
    }

    /// <summary>Fila que esta casca assina — uma instância por fila (entrada da esteira e descoberta).</summary>
    public string QueueName => _queueName;

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

        var message = new DeadLetterMessage(
            args.Message.Body,
            args.Message.DeadLetterReason,
            args.Message.DeadLetterErrorDescription,
            args.Message.MessageId,
            args.Message.SequenceNumber,
            args.Message.EnqueuedTime,
            _queueName);

        // Concluir DEPOIS do tratamento, e não antes nem num `finally`: é essa ordem que faz a exceção pular a
        // conclusão, e é pular a conclusão que salva a falha transitória — a próxima entrega é o conserto dela.
        // Mover esta chamada é o conserto errado (design, D0). O que mudou foi o que chega aqui como exceção:
        // corpo inválido já não é exceção, é `Discarded`, e por isso o laço acabou sem a ordem mudar.
        await handler.HandleAsync(message, args.CancellationToken);
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
