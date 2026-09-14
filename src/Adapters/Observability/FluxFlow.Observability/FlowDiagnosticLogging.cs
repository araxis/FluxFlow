using System.Diagnostics;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Observability;

/// <summary>Connects process-wide runtime diagnostics to a host-owned logger factory.</summary>
public static class FlowDiagnosticLogging
{
    public const string LoggerCategory = "FluxFlow.Processing";

    private static readonly Action<ILogger, string, string, string?, string, Exception?> LogFailure =
        LoggerMessage.Define<string, string, string?, string>(LogLevel.Error,
            new EventId(1, "ProcessingException"),
            "Component {ComponentType} failed processing message {MessageId} (correlation {CorrelationId}, business trace {BusinessTraceId})");

    private static readonly Action<ILogger, string, string, Exception?> LogLifecycleFailure =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(2, "LifecycleException"),
            "Component {ComponentType} failed during {Operation}");

    /// <summary>
    /// Subscribe once per process. Dispose to unsubscribe; the caller retains ownership
    /// of the factory. A callback already in progress may finish after disposal.
    /// </summary>
    public static IDisposable Subscribe(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        return new Subscription(loggerFactory.CreateLogger(LoggerCategory));
    }

    private sealed class Subscription : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ILogger _logger;
        private readonly object _gate = new();
        private readonly List<IDisposable> _listeners = [];
        private readonly IDisposable _discovery;
        private volatile bool _disposed;

        public Subscription(ILogger logger)
        {
            _logger = logger;
            _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != FlowDiagnostics.ListenerName) return;
            lock (_gate)
            {
                if (!_disposed)
                    _listeners.Add(listener.Subscribe(this,
                        name => !_disposed && (name == FlowDiagnostics.ProcessingExceptionEvent ||
                            name == FlowDiagnostics.LifecycleExceptionEvent)));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (_disposed) return;
            try
            {
                if (!_logger.IsEnabled(LogLevel.Error)) return;
                if (value.Key == FlowDiagnostics.ProcessingExceptionEvent &&
                    value.Value is FlowProcessingException failure)
                    LogFailure(_logger, failure.ComponentType, failure.MessageId.ToString(),
                        failure.CorrelationId?.ToString(), failure.TraceId.ToString(), failure.Exception);
                else if (value.Key == FlowDiagnostics.LifecycleExceptionEvent &&
                    value.Value is FlowLifecycleException lifecycle)
                    LogLifecycleFailure(_logger, lifecycle.ComponentType, lifecycle.Operation, lifecycle.Exception);
            }
            catch
            {
                // A failing logging provider must not interrupt processing or other observers.
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }

        public void Dispose()
        {
            IDisposable[] listeners;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                listeners = _listeners.ToArray();
                _listeners.Clear();
            }
            // Discovery holds its own lock during callbacks; never dispose it under _gate.
            _discovery.Dispose();
            foreach (var listener in listeners) listener.Dispose();
        }
    }
}
