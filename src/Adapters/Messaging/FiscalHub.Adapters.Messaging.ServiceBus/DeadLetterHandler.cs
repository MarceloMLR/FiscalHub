using System.Text.Json;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Pipeline;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Consome a dead-letter: uma mensagem que esgotou as tentativas vira um registro visível do
/// documento como não-processável, com o motivo do Service Bus. Separado da casca pra ser testável —
/// e, por isso, é aqui que mora a decisão.
/// <para>
/// A decisão é **distinguir "a mensagem é inválida" de "o tratamento falhou"**, que hoje chegam à casca
/// disfarçadas da mesma exceção. Corpo que não vira referência utilizável nunca vai dar certo: registra no log
/// e devolve <see cref="DeadLetterOutcome.Discarded"/>, **sem lançar**, para a casca concluir e o laço de
/// reentrega acabar. Falha de infraestrutura pode dar certo na próxima entrega: a exceção **propaga**, a casca
/// não conclui, e a reentrega é o conserto. Por isso não há <c>try</c> em volta do store (ADR-0010; design, D0).
/// </para>
/// </summary>
internal sealed class DeadLetterHandler
{
    /// <summary>
    /// Teto do corpo no registro de descarte, em caracteres. O corpo normal é uma referência de algumas centenas de
    /// bytes — o documento fica no Blob (claim-check, ADR-0006). O teto é para o corpo **anormal**, que é justamente
    /// o que chega aqui: uma mensagem pode ter megabytes, e uma linha de log desse tamanho derruba quem a lê.
    /// </summary>
    internal const int BodyLogLimit = 4096;

    private readonly IProcessingStore _store;
    private readonly ILogger<DeadLetterHandler> _logger;

    public DeadLetterHandler(IProcessingStore store, ILogger<DeadLetterHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<DeadLetterOutcome> HandleAsync(DeadLetterMessage message, CancellationToken ct = default)
    {
        DocumentReference? reference;
        try
        {
            reference = message.Body.ToObjectFromJson<DocumentReference>(DocumentQueueSerialization.Options);
        }
        catch (JsonException)
        {
            // Não é JSON, ou é JSON que não casa com a referência — as quatro propriedades `required` fazem o
            // System.Text.Json lançar em vez de produzir um objeto meio-vazio.
            reference = null;
        }

        // Sem tenant e sem chave natural não existe documento a que atribuir o registro: inventar um atribuiria a
        // nota de alguém a outro tenant, e gravar em branco criaria linha que nenhuma consulta encontra (design, D3).
        if (reference is null
            || string.IsNullOrWhiteSpace(reference.TenantId)
            || string.IsNullOrWhiteSpace(reference.NaturalKey))
        {
            Discard(message);
            return DeadLetterOutcome.Discarded;
        }

        await _store.RecordDeadLetterAsync(reference, message.ResolvedReason, ct);
        return DeadLetterOutcome.Recorded;
    }

    /// <summary>
    /// O que fica da mensagem descartada: **um** registro de erro com o corpo e a identidade dela, de uma vez. É com
    /// isso que ela se reconstrói e se republica à mão — não há dead-letter da dead-letter, e o que é concluído some.
    /// <para>
    /// Os campos do transporte entram **crus**, os dois, e não o <see cref="DeadLetterMessage.ResolvedReason"/>. O
    /// piso é do registro no store; escrevê-lo aqui faria o log afirmar que o transporte disse uma coisa que ele não
    /// disse. Quem lê isto está reconstruindo a mensagem, e para esse leitor "não informou motivo" é informação — por
    /// isso o <see cref="DeadLetterMessage.NotReported"/>, que não se confunde com texto do transporte (design, D8).
    /// </para>
    /// </summary>
    private void Discard(DeadLetterMessage message)
    {
        string body = message.Body.ToString();
        string shown = body.Length <= BodyLogLimit ? body : body[..BodyLogLimit];

        _logger.LogError(
            "Mensagem da dead-letter descartada: o corpo não descreve um documento, e nenhuma reentrega o tornaria "
            + "tratável. Fila {Queue}, MessageId {MessageId}, SequenceNumber {SequenceNumber}, enfileirada em "
            + "{EnqueuedAt:o}, DeadLetterReason: {DeadLetterReason}, DeadLetterErrorDescription: "
            + "{DeadLetterErrorDescription}. Corpo ({BodyLength} caracteres, registrados até {BodyLimit}): {Body}",
            message.Queue,
            message.MessageId,
            message.SequenceNumber,
            message.EnqueuedAt,
            DeadLetterMessage.AsReported(message.Reason),
            DeadLetterMessage.AsReported(message.ErrorDescription),
            body.Length,
            BodyLogLimit,
            shown);
    }
}
