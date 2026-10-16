namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Os fatos de uma mensagem na dead-letter, como a casca os lê do transporte. Existe para a decisão sair da casca:
/// o handler recebe os fatos crus — e não um motivo já mastigado — e é ele quem decide o desfecho. É o que torna a
/// decisão testável sem o Service Bus de pé.
/// </summary>
internal readonly record struct DeadLetterMessage(
    BinaryData Body,
    string? Reason,
    string? ErrorDescription,
    string MessageId,
    long SequenceNumber,
    DateTimeOffset EnqueuedAt,
    string Queue)
{
    /// <summary>
    /// O piso do motivo **no registro do store**: nenhum documento `DeadLettered` fica com o campo vazio.
    /// Não serve ao log do descarte — ver <see cref="NotReported"/>.
    /// </summary>
    public const string DefaultReason = "Mensagem movida para dead-letter.";

    /// <summary>
    /// Como um campo que o transporte **não** preencheu aparece no log do descarte. Deliberadamente **não** é o
    /// <see cref="DefaultReason"/>: escrever o piso ali atribuiria ao transporte uma frase que ele não disse. Os
    /// parênteses e a minúscula existem para que não se confunda com texto vindo do transporte (design, D8).
    /// </summary>
    public const string NotReported = "(não informado)";

    /// <summary>O motivo desta mensagem, já com o piso aplicado. É o que vai para o store, e só para ele.</summary>
    public string ResolvedReason => ResolveReason(Reason, ErrorDescription);

    /// <summary>
    /// Um campo cru do transporte como ele vai para o log do descarte: o texto que chegou, ou
    /// <see cref="NotReported"/> quando não chegou nada.
    /// <para>
    /// **Aqui não há piso, e isso é o oposto do <see cref="ResolveReason"/> de propósito.** O leitor deste texto não
    /// pergunta "por que essa nota parou?", e sim "o que exatamente chegou, para eu reconstruir e republicar esta
    /// mensagem?". Para ele, "o transporte não informou motivo" é informação — e o texto padrão a esconderia, ainda
    /// por cima afirmando que o transporte disse algo. O colapso nulo/vazio é **por leitor**, e não global
    /// (design, D8).
    /// </para>
    /// </summary>
    public static string AsReported(string? value) => Filled(value) ?? NotReported;

    /// <summary>
    /// Resolve o motivo que vai para o registro: <c>DeadLetterReason</c>, senão <c>DeadLetterErrorDescription</c>,
    /// senão o texto padrão.
    /// <para>
    /// **Ausente e em branco são a mesma coisa neste campo**, e isso é decisão. A distinção nulo × vazio é real, e o
    /// projeto a respeita onde há leitor com duas perguntas. Aqui o leitor é um só e a pergunta é uma só — "por que
    /// essa nota parou?" —, e "foi dito que não há motivo" e "nunca foi dito" produzem a mesma tela vazia. Logo os
    /// dois colapsam, e o texto padrão é o piso. Só-espaços conta como em branco. A distinção continua existindo na
    /// origem, no Service Bus; ela só não atravessa para o registro **do store**. Para o log do descarte, que tem
    /// outro leitor, ela atravessa inteira — ver <see cref="AsReported"/> (design, D8).
    /// </para>
    /// </summary>
    public static string ResolveReason(string? reason, string? errorDescription) =>
        Filled(reason) ?? Filled(errorDescription) ?? DefaultReason;

    private static string? Filled(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// O desfecho de uma mensagem da dead-letter. Não há um terceiro: "o tratamento falhou" não é desfecho, é exceção —
/// e é ela que faz a casca não concluir a mensagem, preservando-a para a próxima entrega.
/// </summary>
internal enum DeadLetterOutcome
{
    /// <summary>O corpo era uma referência utilizável: o documento virou registro visível.</summary>
    Recorded,

    /// <summary>O corpo não era: a mensagem foi registrada no log e é concluída, para parar de ser reentregue.</summary>
    Discarded,
}
