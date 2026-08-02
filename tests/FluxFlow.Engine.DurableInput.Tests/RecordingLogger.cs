using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.DurableInput.Tests;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
                return _messages.ToArray();
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _messages.Add(formatter(state, exception));
            if (exception is not null)
                _messages.Add(exception.ToString());
        }
    }
}
