using Azure.Messaging.ServiceBus;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// O ponta a ponta contra o emulador REAL do Service Bus (o <c>servicebus</c> do <c>docker-compose.yml</c>): é o
/// único teste que prova o **efeito** da fatia — "a mensagem envenenada não volta a ser entregue sem fim" — e o
/// único que executa as quatro linhas da casca (<c>OnMessageAsync</c>), que não tem teste unitário por decisão
/// (design, D1 e D2).
/// <para>
/// Opt-in: só roda com <c>FISCALHUB_SB_EMULATOR</c> definida; sem ela aparece como **pulado** e não toca a rede —
/// CI inclusive. É o padrão do <c>KeyVaultEmulatorIntegrationTests</c> e do <c>D365ChangeFeedIntegrationTests</c>.
/// </para>
/// <para>
/// **Efeito colateral aceito:** ele usa a fila real do emulador local. Uma mensagem alheia pendente na
/// <c>documents-in</c> tem a contagem de entrega incrementada pelo laço que esgota a nossa, e uma mensagem alheia
/// pendente na dead-letter dela é consumida pela casca. Num emulador de dev isso é aceitável; contra um namespace
/// de verdade, não — por isso o teste é opt-in e aponta para o emulador.
/// </para>
/// </summary>
public class DeadLetterEmulatorTests
{
    private const string EmulatorConnection =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private const string Queue = "documents-in";

    [DeadLetterEmulatorFact]
    public async Task A_poison_message_is_consumed_once_and_leaves_the_dead_letter()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var client = new ServiceBusClient(EmulatorConnection);
        string messageId = $"poison-{Guid.NewGuid():N}";

        // 1) Um corpo que nunca vai desserializar, na fila de verdade.
        await using (ServiceBusSender sender = client.CreateSender(Queue))
        {
            await sender.SendMessageAsync(
                new ServiceBusMessage(BinaryData.FromString("isto não é uma referência de documento"))
                {
                    MessageId = messageId,
                },
                cts.Token);
        }

        // 2) Deixa a entrega esgotar: é o caminho de produção até a dead-letter (o consumidor da esteira falha, o
        //    transporte reconta, e no MaxDeliveryCount a mensagem é movida — ninguém nosso chama DeadLetterMessageAsync).
        await ExhaustDeliveryAsync(client, messageId, cts.Token);
        Assert.NotNull(await PeekDeadLetterAsync(client, messageId, cts.Token));

        // 3) Sobe a casca — a mesma classe que o host registra.
        await using ServiceProvider services = BuildServices();
        var logger = (CapturingLogger<DeadLetterTriggerService>)services.GetRequiredService<ILogger<DeadLetterTriggerService>>();
        var handlerLog = (CapturingLogger<DeadLetterHandler>)services.GetRequiredService<ILogger<DeadLetterHandler>>();
        var shell = new DeadLetterTriggerService(client, services, Queue, logger);

        await shell.StartAsync(cts.Token);
        try
        {
            // 4) E ela some: concluída na dead-letter, que não tem dead-letter.
            await WaitUntilGoneAsync(client, messageId, cts.Token);
        }
        finally
        {
            await shell.StopAsync(CancellationToken.None);
        }

        Assert.Null(await PeekDeadLetterAsync(client, messageId, cts.Token));

        // Consumida **uma vez**: um registro de descarte com o identificador dela, e não um por entrega (o D2).
        Assert.Single(handlerLog.Errors, e => e.Text.Contains(messageId, StringComparison.Ordinal));
        // E a casca não registrou erro nenhum: o corpo inválido deixou de ser exceção.
        Assert.Empty(logger.Errors);
        Assert.Empty(services.GetRequiredService<RecordingStore>().DeadLettered);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddSingleton<RecordingStore>();
        services.AddSingleton<IProcessingStore>(sp => sp.GetRequiredService<RecordingStore>());
        services.AddScoped<DeadLetterHandler>();
        return services.BuildServiceProvider();
    }

    /// <summary>Recebe e abandona até a nossa mensagem sair da fila principal — o abandono reconta a entrega.</summary>
    private static async Task ExhaustDeliveryAsync(ServiceBusClient client, string messageId, CancellationToken ct)
    {
        await using ServiceBusReceiver receiver = client.CreateReceiver(Queue);

        for (int round = 0; round < 40; round++)
        {
            ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), ct);
            if (message is null)
            {
                // Nada mais na fila principal: ou ela foi para a dead-letter, ou nunca chegou.
                return;
            }

            await receiver.AbandonMessageAsync(message, cancellationToken: ct);
        }

        Assert.Fail($"A mensagem {messageId} não foi para a dead-letter em 40 entregas — confira o MaxDeliveryCount do docker/servicebus/Config.json.");
    }

    private static async Task WaitUntilGoneAsync(ServiceBusClient client, string messageId, CancellationToken ct)
    {
        for (int round = 0; round < 120; round++)
        {
            if (await PeekDeadLetterAsync(client, messageId, ct) is null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        Assert.Fail($"A mensagem {messageId} continuou na dead-letter depois de 60s — é o laço do D2 de volta.");
    }

    /// <summary>Espia a dead-letter (sem travar nada) procurando a nossa mensagem. Receiver novo a cada chamada: o peek tem cursor.</summary>
    private static async Task<ServiceBusReceivedMessage?> PeekDeadLetterAsync(ServiceBusClient client, string messageId, CancellationToken ct)
    {
        await using ServiceBusReceiver receiver = client.CreateReceiver(
            Queue,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        IReadOnlyList<ServiceBusReceivedMessage> peeked = await receiver.PeekMessagesAsync(100, fromSequenceNumber: 0, ct);
        return peeked.FirstOrDefault(m => m.MessageId == messageId);
    }

    /// <summary>Store que registra o que foi gravado — aqui ele tem que ficar vazio: descarte não grava.</summary>
    private sealed class RecordingStore : IProcessingStore
    {
        private readonly List<string> _deadLettered = [];

        public IReadOnlyList<string> DeadLettered
        {
            get
            {
                lock (_deadLettered)
                {
                    return [.. _deadLettered];
                }
            }
        }

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        {
            lock (_deadLettered)
            {
                _deadLettered.Add(reference.NaturalKey);
            }

            return Task.CompletedTask;
        }

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default) => Task.FromResult(false);
        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PendingIntegration>>([]);
        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordSavedAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }
}

public sealed class DeadLetterEmulatorFactAttribute : FactAttribute
{
    public DeadLetterEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FISCALHUB_SB_EMULATOR")))
        {
            Skip = "Ponta a ponta na dead-letter: suba o 'servicebus' do docker compose (docker compose up -d servicebus sql) e defina FISCALHUB_SB_EMULATOR=1.";
        }
    }
}
