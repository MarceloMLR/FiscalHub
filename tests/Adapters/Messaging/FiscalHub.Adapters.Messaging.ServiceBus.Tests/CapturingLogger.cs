using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// Logger escrito à mão (sem libs de mock): guarda nível, texto formatado e exceção de cada entrada.
/// Cópia da forma do <c>FiscalHub.Adapters.Outbound.Avalara.Tests/CapturingLogger.cs</c> — projeto de teste não
/// referencia projeto de teste, então é cópia, e não reuso.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Text, Exception? Error)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Text, Exception? Error)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Só as entradas de erro — é o nível que o descarte de mensagem inválida exige.</summary>
    public IReadOnlyList<(LogLevel Level, string Text, Exception? Error)> Errors =>
        [.. Entries.Where(e => e.Level == LogLevel.Error)];

    /// <summary>Tudo o que foi logado, num texto só.</summary>
    public string All => string.Join("\n", Entries.Select(e => e.Text));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string text = formatter(state, exception);
        lock (_entries)
        {
            _entries.Add((logLevel, text, exception));
        }
    }
}
