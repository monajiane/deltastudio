using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace DeltaStudio.App.Services;

/// <summary>
/// In-memory log pipe bridged to the Output pane. Structured log entries from every layer
/// (engine, protocols, MCP) land here through <see cref="UiLogServiceExtensions.AddUiLogSink"/>.
/// </summary>
public sealed class UiLogService
{
    private readonly ObservableCollection<string> _lines = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    /// <summary>Creates the sink; captures the UI dispatcher (constructed on the UI thread at startup).</summary>
    public UiLogService() => _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    /// <summary>Ring buffer of formatted log lines (UI-thread safe: mutations are marshalled).</summary>
    public ObservableCollection<string> Lines => _lines;

    /// <summary>Append one formatted entry from any thread.</summary>
    public void Append(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        void Add()
        {
            _lines.Add(line);
            while (_lines.Count > 5000)
            {
                _lines.RemoveAt(0);
            }
        }

        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            Add();
        }
        else
        {
            _dispatcher.TryEnqueue(() => Add());
        }
    }
}

/// <summary>Forwards <see cref="ILogger"/> traffic into the UI log sink.</summary>
internal sealed class UiLogProvider : ILoggerProvider
{
    private readonly UiLogService _sink;

    public UiLogProvider(UiLogService sink) => _sink = sink;

    public ILogger CreateLogger(string categoryName) => new ForwardingLogger(_sink, categoryName);

    public void Dispose()
    {
    }

    private sealed class ForwardingLogger(UiLogService sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string text = formatter(state, exception);
            if (exception is not null)
            {
                text += $" | {exception.GetType().Name}: {exception.Message}";
            }

            sink.Append(logLevel.ToString(), $"{category}: {text}");
        }
    }
}

/// <summary>Registers the UI log sink with logging builders.</summary>
public static class UiLogServiceExtensions
{
    /// <summary>Bridge <see cref="ILogger"/> output into <see cref="UiLogService"/>.</summary>
    public static ILoggingBuilder AddUiLogSink(this ILoggingBuilder builder, UiLogService sink)
    {
        builder.AddProvider(new UiLogProvider(sink));
        return builder;
    }
}
