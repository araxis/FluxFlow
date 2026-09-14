using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace FluxFlow.Observability;

public static class FlowTelemetryExtensions
{
    /// <summary>
    /// Registers the standard SDK pipelines and host-owned diagnostic logging.
    /// Configure resources/exporters on the returned builder. Register before flow hosted services.
    /// </summary>
    public static OpenTelemetryBuilder AddFluxFlowTelemetry(this IServiceCollection services)
    {
        var telemetry = services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(FlowDiagnostics.ListenerName))
            .WithMetrics(metrics => metrics.AddMeter(FlowRuntimeMetrics.MeterName))
            .WithLogging();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DiagnosticLoggingService>());
        return telemetry;
    }

    private sealed class DiagnosticLoggingService(ILoggerFactory factory, LoggerProvider loggerProvider) : IHostedService, IDisposable
    {
        private IDisposable? _subscription;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _subscription ??= FlowDiagnosticLogging.Subscribe(factory);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            // Flush while host services still exist. A first OTLP log batch may
            // create its HTTP client lazily, which is too late during DI disposal.
            try { loggerProvider.ForceFlush(5_000); }
            catch { /* Exporter failures must not interrupt host shutdown. */ }
            return Task.CompletedTask;
        }
        public void Dispose() => Interlocked.Exchange(ref _subscription, null)?.Dispose();
    }
}
