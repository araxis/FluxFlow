using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Signals;

public static class ApplicationRuntimeInstrumentation
{
    public const string MeterName = "FluxFlow.Engine.Runtime";
    public const string DiagnosticSourceName = "FluxFlow.Engine.Runtime";
    public const string DiagnosticEventName = "FluxFlow.Engine.Runtime.Diagnostic";
    public const string SystemEventName = "FluxFlow.Engine.Runtime.SystemEvent";

    internal static readonly Meter Meter = new(MeterName);
    internal static readonly DiagnosticListener DiagnosticSource = new(DiagnosticSourceName);
    internal static readonly Counter<long> DiagnosticsAccepted =
        Meter.CreateCounter<long>("fluxflow.runtime.diagnostics.accepted");
    internal static readonly Counter<long> DiagnosticsDropped =
        Meter.CreateCounter<long>("fluxflow.runtime.diagnostics.dropped");
    internal static readonly Counter<long> SystemEventsAccepted =
        Meter.CreateCounter<long>("fluxflow.runtime.system_events.accepted");
    internal static readonly Counter<long> SystemEventsRejected =
        Meter.CreateCounter<long>("fluxflow.runtime.system_events.rejected");
    internal static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("fluxflow.runtime.request.duration", unit: "ms");

    internal static void RecordDiagnostic(FlowMessage message, FlowEvent diagnostic, ILogger? logger)
    {
        RecordDiagnosticMetrics(diagnostic);
        RecordLog(diagnostic, logger);
        WriteEvent(DiagnosticEventName, message);
    }

    private static void RecordDiagnosticMetrics(FlowEvent diagnostic)
    {
        if (!DiagnosticsAccepted.Enabled && !RequestDuration.Enabled) return;
        var tags = CreateTags(diagnostic);
        try { DiagnosticsAccepted.Add(1, tags); }
        catch { /* A counter listener must not suppress the other observations. */ }

        if (diagnostic.Kind == FlowEventKind.Diagnostic &&
            diagnostic.Name == ApplicationDiagnosticNames.RequestCompleted &&
            diagnostic.Measurements.TryGetValue("duration.ms", out var duration) &&
            double.IsFinite(duration) && duration >= 0)
        {
            try { RequestDuration.Record(duration, tags); }
            catch { /* A duration listener must not suppress logs or events. */ }
        }
    }

    private static void RecordLog(FlowEvent diagnostic, ILogger? logger)
    {
        if (logger is null) return;
        try
        {
            var level = MapLevel(diagnostic.Level);
            if (level != LogLevel.None && logger.IsEnabled(level))
                logger.Log(level, new EventId(0, diagnostic.Name),
                    "{DiagnosticName}: {DiagnosticMessage}",
                    diagnostic.Name, diagnostic.Message ?? diagnostic.Name);
        }
        catch { /* A logger must not suppress diagnostic event delivery. */ }
    }

    internal static void RecordDiagnosticDrop()
    {
        try
        {
            DiagnosticsDropped.Add(1);
        }
        catch
        {
            // Host meter listeners are isolated from runtime processing.
        }
    }

    internal static void RecordSystemEvent(
        FlowMessage message,
        bool accepted)
    {
        try
        {
            if (accepted)
                SystemEventsAccepted.Add(1);
            else
                SystemEventsRejected.Add(1);
        }
        catch
        {
            // A meter listener must not suppress the accepted event.
        }
        if (accepted) WriteEvent(SystemEventName, message);
    }

    private static void WriteEvent(string name, FlowMessage message)
    {
        try
        {
            if (DiagnosticSource.IsEnabled(name)) DiagnosticSource.Write(name, message);
        }
        catch { /* Host event observers cannot change runtime outcomes. */ }
    }

    private static TagList CreateTags(FlowEvent diagnostic)
        => new()
        {
            { "flow.diagnostic.kind", Enum.IsDefined(diagnostic.Kind) ? diagnostic.Kind.ToString() : "Unknown" },
            { "flow.diagnostic.level", Enum.IsDefined(diagnostic.Level) ? diagnostic.Level.ToString() : "Unknown" }
        };

    private static LogLevel MapLevel(FlowEventLevel level)
        => level switch
        {
            FlowEventLevel.Trace => LogLevel.Trace,
            FlowEventLevel.Information => LogLevel.Information,
            FlowEventLevel.Warning => LogLevel.Warning,
            FlowEventLevel.Error => LogLevel.Error,
            _ => LogLevel.None
        };
}
