using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>Logger escrito à mão (sem libs de mock): guarda o texto formatado de cada entrada, com a exceção.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Text)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Text)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Tudo o que foi logado, num texto só: para conferir que nada sensível aparece.</summary>
    public string All => string.Join("\n", Entries.Select(e => e.Text));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string text = formatter(state, exception) + (exception is null ? string.Empty : $" | {exception}");
        lock (_entries)
        {
            _entries.Add((logLevel, text));
        }
    }
}
