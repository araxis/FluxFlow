using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace FluxFlow.Components.Logging;

/// <summary>Keeps SDK record creation and cleanup intact while exposing synchronous exporter failures to the caller.</summary>
internal sealed class ExportReportingLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _factory;
    private readonly ReportingProcessor _processor;
    private int _disposed;

    internal ExportReportingLoggerFactory(BaseExporter<LogRecord> exporter, ResourceBuilder resource, LogLevel minimum)
    {
        _processor = new ReportingProcessor(exporter);
        try
        {
            _factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(minimum).AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resource);
                options.IncludeScopes = true;
                options.AddProcessor(_processor);
            }));
        }
        catch { _processor.Dispose(); throw; }
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new ReportingLogger(_factory.CreateLogger(categoryName), this);
    }

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("This logger is dedicated to one OTLP exporter.");
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _factory.Dispose();
    }

    private sealed class ExportCall
    {
        internal int Count;
        internal bool Succeeded;
        internal Exception? Error;
    }

    private sealed class ReportingLogger(ILogger inner, ExportReportingLoggerFactory owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref owner._disposed) != 0, owner);
            if (!inner.IsEnabled(level)) return; // Intentional level filtering is not an export failure.
            var previous = owner._processor.Current.Value;
            var call = new ExportCall();
            owner._processor.Current.Value = call;
            try
            {
                inner.Log(level, eventId, state, exception, formatter);
                if (call.Count != 1 || !call.Succeeded)
                    throw new InvalidOperationException("OTLP log export failed, timed out or was suppressed.", call.Error);
            }
            finally { owner._processor.Current.Value = previous; }
        }
    }

    private sealed class ReportingProcessor(BaseExporter<LogRecord> exporter) : BaseExportProcessor<LogRecord>(exporter)
    {
        internal readonly AsyncLocal<ExportCall?> Current = new();
        private readonly object _gate = new();
        private bool _closed;

        protected override void OnExport(LogRecord data)
        {
            if (Current.Value is not { } call) return;
            lock (_gate) // The native exporter reuses its serialization buffer across calls.
            {
                call.Count++;
                if (_closed) return;
                try { call.Succeeded = exporter.Export(new Batch<LogRecord>(data)) == ExportResult.Success; }
                catch (Exception error) { call.Error = error; }
                // Never throw through the processor or retain the SDK's pooled LogRecord.
            }
        }

        protected override bool OnShutdown(int timeoutMilliseconds)
        {
            lock (_gate) { _closed = true; return base.OnShutdown(timeoutMilliseconds); }
        }
        protected override void Dispose(bool disposing)
        {
            lock (_gate) { _closed = true; base.Dispose(disposing); }
        }
    }
}
