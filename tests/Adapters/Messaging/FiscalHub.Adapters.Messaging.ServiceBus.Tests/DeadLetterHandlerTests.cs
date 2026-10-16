using System.Globalization;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// Especifica o consumidor de dead-letter, que é onde mora a distinção desta capability: **a mensagem é inválida**
/// (nenhuma reentrega a tornaria tratável → descarta, registra no log, devolve <c>Discarded</c>) × **o tratamento
/// falhou** (a próxima entrega pode consertar → a exceção propaga, e a casca não conclui). O store é falso e o
/// logger é falso; nenhuma lib de mock.
/// </summary>
public class DeadLetterHandlerTests
{
    private static readonly DateTimeOffset EnqueuedAt = new(2026, 10, 16, 9, 30, 15, TimeSpan.Zero);

    // ---- a referência válida continua virando registro (o que a fatia NÃO muda) ----

    [Fact]
    public async Task Handle_records_dead_letter_with_reason()
    {
        var store = new FakeStore();
        DeadLetterHandler handler = HandlerFor(store);

        DeadLetterOutcome outcome = await handler.HandleAsync(Message(Reference()));

        Assert.Equal(DeadLetterOutcome.Recorded, outcome);
        Assert.NotNull(store.DeadLettered);
        Assert.Equal("nfe-1", store.DeadLettered!.Value.Key);
        Assert.Equal("MaxDeliveryCountExceeded", store.DeadLettered.Value.Reason);
    }

    [Fact]
    public async Task Discovery_queue_reference_is_recorded_as_dead_lettered_too()
    {
        // Referência publicada pelo feed do D365 na documents-discovered: a mesma dead-letter visível (ADR-0010).
        var store = new FakeStore();
        DeadLetterHandler handler = HandlerFor(store);
        DocumentReference discovered = Reference() with { NaturalKey = "brmf|BRMF21-10000026", Locator = "d365/brmf/35637156582", Origin = "Dynamics365" };

        DeadLetterOutcome outcome = await handler.HandleAsync(Message(discovered, queue: "documents-discovered"));

        Assert.Equal(DeadLetterOutcome.Recorded, outcome);
        Assert.Equal(("brmf|BRMF21-10000026", "MaxDeliveryCountExceeded"), store.DeadLettered);
    }

    // ---- a mensagem inválida é descartada (D4: três portas) ----

    [Fact]
    public async Task Body_that_is_not_json_is_discarded_and_logged_once()
    {
        var store = new FakeStore();
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(store, logger);

        DeadLetterOutcome outcome = await handler.HandleAsync(Message("isto não é JSON <xml/>"));

        Assert.Equal(DeadLetterOutcome.Discarded, outcome);
        Assert.Null(store.DeadLettered);
        // Um registro por mensagem — e não um por entrega, que é o D2.
        (LogLevel Level, string Text, Exception? Error) only = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, only.Level);
    }

    [Fact]
    public async Task Null_body_is_discarded_and_never_throws()
    {
        // Era o `Handle_throws_on_empty_body`: o requisito publicado hoje é o próprio defeito, e aqui ele inverte.
        var store = new FakeStore();
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(store, logger);

        DeadLetterOutcome outcome = await handler.HandleAsync(Message("null"));

        Assert.Equal(DeadLetterOutcome.Discarded, outcome);
        Assert.Null(store.DeadLettered);
        Assert.Single(logger.Errors);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tenantId\":\"t\"}")]
    [InlineData("{\"tenantId\":\"t\",\"type\":\"GoodsInvoice55\",\"naturalKey\":\"k\"}")]
    public async Task Json_missing_a_required_field_is_discarded(string body)
    {
        // D4, porta 1: as quatro propriedades `required` fazem o System.Text.Json lançar, em vez de
        // produzir um objeto meio-vazio. É o que torna esta porta larga.
        var store = new FakeStore();
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(store, logger);

        DeadLetterOutcome outcome = await handler.HandleAsync(Message(body));

        Assert.Equal(DeadLetterOutcome.Discarded, outcome);
        Assert.Null(store.DeadLettered);
        Assert.Single(logger.Errors);
    }

    [Theory]
    [InlineData("", "nfe-1")]
    [InlineData("   ", "nfe-1")]
    [InlineData("tenant-a", "")]
    [InlineData("tenant-a", "  ")]
    [InlineData("", "")]
    public async Task Blank_tenant_or_natural_key_is_discarded_and_nothing_is_stored(string tenantId, string naturalKey)
    {
        // D4, porta 3: passa pelo `required` (a propriedade está presente) e hoje grava uma linha órfã,
        // que nenhuma consulta com `WHERE TenantId = @t` encontra.
        var store = new FakeStore();
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(store, logger);
        DocumentReference blank = Reference() with { TenantId = tenantId, NaturalKey = naturalKey };

        DeadLetterOutcome outcome = await handler.HandleAsync(Message(blank));

        Assert.Equal(DeadLetterOutcome.Discarded, outcome);
        Assert.Null(store.DeadLettered);
        Assert.Single(logger.Errors);
    }

    // ---- o que fica escrito do descarte ----

    [Fact]
    public async Task The_discard_record_carries_the_body_the_queue_and_the_message_identity()
    {
        // Campo a campo, e não "logou alguma coisa": a asserção frouxa passaria com o log de hoje.
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(new FakeStore(), logger);
        const string Body = "{\"isto\":\"não é uma referência\"}";
        var message = new DeadLetterMessage(
            BinaryData.FromString(Body),
            Reason: "MaxDeliveryCountExceeded",
            ErrorDescription: null,
            MessageId: "msg-4f2a",
            SequenceNumber: 987654321,
            EnqueuedAt: EnqueuedAt,
            Queue: "documents-discovered");

        await handler.HandleAsync(message);

        string record = Assert.Single(logger.Errors).Text;
        Assert.Contains(Body, record, StringComparison.Ordinal);
        Assert.Contains("documents-discovered", record, StringComparison.Ordinal);
        Assert.Contains("msg-4f2a", record, StringComparison.Ordinal);
        Assert.Contains("987654321", record, StringComparison.Ordinal);
        Assert.Contains(EnqueuedAt.ToString("o", CultureInfo.InvariantCulture), record, StringComparison.Ordinal);
        Assert.Contains("MaxDeliveryCountExceeded", record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_discard_record_carries_the_raw_transport_fields_and_not_the_resolved_reason()
    {
        // O leitor do descarte reconstrói a mensagem: ele quer os dois campos como chegaram. O `ResolvedReason`
        // entrega **um** só — com o `DeadLetterReason` preenchido, a descrição é descartada pela cadeia do piso.
        // Por isso a asserção sobre a descrição é a que fica vermelha se alguém devolver o resolvido para o log.
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(new FakeStore(), logger);
        const string Description = "Message could not be consumed after 5 delivery attempts.";

        await handler.HandleAsync(Message(
            "isto não é JSON <xml/>",
            reason: "MaxDeliveryCountExceeded",
            errorDescription: Description));

        string record = Assert.Single(logger.Errors).Text;
        Assert.Contains("DeadLetterReason: MaxDeliveryCountExceeded", record, StringComparison.Ordinal);
        Assert.Contains($"DeadLetterErrorDescription: {Description}", record, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "\t")]
    public async Task A_transport_that_said_nothing_is_not_quoted_saying_the_default_reason(string? reason, string? errorDescription)
    {
        // O defeito que este teste tranca: com `ResolvedReason` no log, o registro escrevia o texto padrão do piso
        // **atribuindo-o ao transporte** — afirmava que ele disse algo que não disse. A asserção frouxa ("tem alguma
        // coisa no campo") passa com e sem o conserto; esta não. O piso do store não muda (D8).
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(new FakeStore(), logger);

        await handler.HandleAsync(Message("isto não é JSON <xml/>", reason, errorDescription));

        string record = Assert.Single(logger.Errors).Text;
        Assert.DoesNotContain(DeadLetterMessage.DefaultReason, record, StringComparison.Ordinal);
        Assert.Contains($"DeadLetterReason: {DeadLetterMessage.NotReported}", record, StringComparison.Ordinal);
        Assert.Contains($"DeadLetterErrorDescription: {DeadLetterMessage.NotReported}", record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_past_the_limit_is_cut_and_the_record_says_the_original_size()
    {
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(new FakeStore(), logger);
        string huge = new('x', DeadLetterHandler.BodyLogLimit + 777);

        await handler.HandleAsync(Message(huge));

        string record = Assert.Single(logger.Errors).Text;
        Assert.DoesNotContain(huge, record, StringComparison.Ordinal);
        Assert.Contains(huge[..DeadLetterHandler.BodyLogLimit], record, StringComparison.Ordinal);
        Assert.Contains(huge.Length.ToString(CultureInfo.InvariantCulture), record, StringComparison.Ordinal);
    }

    // ---- o tratamento que falhou NÃO é descarte ----

    [Fact]
    public async Task A_store_that_fails_propagates_and_is_not_swallowed()
    {
        // A outra metade da distinção, e o teste que impede o conserto errado ("concluir sempre"): a exceção
        // tem que subir, porque é ela que faz a casca não concluir — e é a reentrega que conserta o transitório.
        var store = new FakeStore { Failure = new InvalidOperationException("SQL fora do ar.") };
        var logger = new CapturingLogger<DeadLetterHandler>();
        DeadLetterHandler handler = HandlerFor(store, logger);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(Message(Reference())));

        Assert.Equal("SQL fora do ar.", error.Message);
        // Não vira descarte, e não é engolido num log de erro no lugar da exceção (D5).
        Assert.Empty(logger.Errors);
    }

    // ---- o motivo em branco não é motivo (D5) ----

    [Theory]
    [InlineData("MaxDeliveryCountExceeded", "a descrição", "MaxDeliveryCountExceeded")]
    [InlineData("MaxDeliveryCountExceeded", null, "MaxDeliveryCountExceeded")]
    [InlineData(null, "a descrição", "a descrição")]
    [InlineData("", "a descrição", "a descrição")]
    [InlineData("   ", "a descrição", "a descrição")]
    [InlineData(null, null, "Mensagem movida para dead-letter.")]
    [InlineData("", "", "Mensagem movida para dead-letter.")]
    [InlineData("  ", "   ", "Mensagem movida para dead-letter.")]
    public void Blank_counts_as_absent_when_the_reason_is_resolved(string? reason, string? errorDescription, string expected)
    {
        // Os casos com `null` passam na cadeia de `??` de hoje; os com vazio e só-espaços, não.
        // É essa assimetria que prova que o teste cobre vazio, e não só nulo.
        Assert.Equal(expected, DeadLetterMessage.ResolveReason(reason, errorDescription));
    }

    [Fact]
    public async Task A_blank_reason_never_reaches_the_store()
    {
        var store = new FakeStore();
        DeadLetterHandler handler = HandlerFor(store);

        await handler.HandleAsync(Message(Reference(), reason: "   ", errorDescription: ""));

        Assert.Equal(("nfe-1", "Mensagem movida para dead-letter."), store.DeadLettered);
    }

    // ---- fixtures ----

    private static DeadLetterHandler HandlerFor(FakeStore store, CapturingLogger<DeadLetterHandler>? logger = null) =>
        new(store, logger ?? new CapturingLogger<DeadLetterHandler>());

    private static DeadLetterMessage Message(
        string body,
        string? reason = "MaxDeliveryCountExceeded",
        string? errorDescription = null,
        string queue = "documents-in") =>
        new(BinaryData.FromString(body), reason, errorDescription, "msg-1", 42, EnqueuedAt, queue);

    private static DeadLetterMessage Message(
        DocumentReference reference,
        string? reason = "MaxDeliveryCountExceeded",
        string? errorDescription = null,
        string queue = "documents-in") =>
        new(
            BinaryData.FromObjectAsJson(reference, DocumentQueueSerialization.Options),
            reason,
            errorDescription,
            "msg-1",
            42,
            EnqueuedAt,
            queue);

    private static DocumentReference Reference() => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "nfe-1",
        Locator = "nfe/nfe-1.xml",
    };

    private sealed class FakeStore : IProcessingStore
    {
        public (string Key, string Reason)? DeadLettered { get; private set; }

        /// <summary>Quando preenchido, a gravação quebra — é o "o tratamento falhou" da distinção.</summary>
        public Exception? Failure { get; init; }

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            DeadLettered = (reference.NaturalKey, reason);
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
