using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Signals;

public static class ApplicationRuntimeInstrumentation
{
    public const string ActivitySourceName = "FluxFlow.Engine.Runtime";
    public const string MeterName = "FluxFlow.Engine.Runtime";
    public const string DiagnosticSourceName = "FluxFlow.Engine.Runtime";
    public const string DiagnosticEventName = "FluxFlow.Engine.Runtime.Diagnostic";
    public const string SystemEventName = "FluxFlow.Engine.Runtime.SystemEvent";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
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
    internal static readonly Histogram<double> DiagnosticMeasurements =
        Meter.CreateHistogram<double>("fluxflow.runtime.diagnostic.measurement");

    internal static void RecordDiagnostic(
        FlowMessage message,
        ILogger? logger)
    {
        try
        {
            var diagnostic = message.ToFlowEvent();
            var tags = CreateTags(diagnostic, message);
            DiagnosticsAccepted.Add(1, tags);

            if (diagnostic.Measurements.TryGetValue("value", out var measurement))
                DiagnosticMeasurements.Record(measurement, tags);

            logger?.Log(
                MapLevel(diagnostic.Level),
                new EventId(0, diagnostic.Name),
                "{DiagnosticName}: {DiagnosticMessage}",
                diagnostic.Name,
                diagnostic.Message ?? diagnostic.Name);

            if (DiagnosticSource.IsEnabled(DiagnosticEventName))
                DiagnosticSource.Write(DiagnosticEventName, message);

            var operationKind = Dimension(diagnostic, "kind");
            if (ShouldCreateActivity(operationKind))
            {
                using var activity = ActivitySource.StartActivity(
                    diagnostic.Name,
                    operationKind is "Input" or "Output"
                        ? ActivityKind.Producer
                        : ActivityKind.Internal);
                if (activity is not null)
                {
                    activity.SetTag("flow.trace_id", message.TraceId.Value);
                    activity.SetTag("flow.message_id", message.MessageId.Value);
                    activity.SetTag("flow.diagnostic.kind", operationKind);
                    activity.SetTag("flow.diagnostic.subject", Dimension(diagnostic, "subject"));
                    activity.SetTag("flow.diagnostic.duration_ms",
                        diagnostic.Measurements.GetValueOrDefault("duration.ms"));
                }
            }
        }
        catch
        {
            // Host observability providers must never fault runtime processing.
        }
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

            if (accepted && DiagnosticSource.IsEnabled(SystemEventName))
                DiagnosticSource.Write(SystemEventName, message);
        }
        catch
        {
            // DiagnosticSource subscribers are host-owned and isolated.
        }
    }

    private static TagList CreateTags(
        FlowEvent diagnostic,
        FlowMessage message)
    {
        var tags = new TagList
        {
            { "flow.diagnostic.name", diagnostic.Name },
            { "flow.diagnostic.kind", Dimension(diagnostic, "kind") ?? diagnostic.Kind.ToString() },
            { "flow.diagnostic.level", diagnostic.Level.ToString() },
            { "flow.diagnostic.subject", Dimension(diagnostic, "subject") },
            { "flow.trace_id", message.TraceId.Value }
        };
        var unit = Dimension(diagnostic, "unit");
        if (!string.IsNullOrWhiteSpace(unit))
            tags.Add("flow.diagnostic.unit", unit);
        return tags;
    }

    private static string? Dimension(FlowEvent @event, string name)
        => @event.Dimensions.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static bool ShouldCreateActivity(string? kind)
        => kind is "Input" or "Output" or "Timing" or "Trace";

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
