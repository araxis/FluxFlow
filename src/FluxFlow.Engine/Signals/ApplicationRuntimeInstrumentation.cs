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
        FlowMessage<ApplicationDiagnostic> message,
        ILogger? logger)
    {
        try
        {
            var diagnostic = message.Payload;
            var tags = CreateTags(diagnostic, message);
            DiagnosticsAccepted.Add(1, tags);

            if (diagnostic.Measurement is { } measurement)
                DiagnosticMeasurements.Record(measurement, tags);

            logger?.Log(
                MapLevel(diagnostic.Level),
                new EventId(0, diagnostic.Name),
                "{DiagnosticName}: {DiagnosticMessage}",
                diagnostic.Name,
                diagnostic.Message ?? diagnostic.Name);

            if (DiagnosticSource.IsEnabled(DiagnosticEventName))
                DiagnosticSource.Write(DiagnosticEventName, message);

            if (ShouldCreateActivity(diagnostic.Kind))
            {
                using var activity = ActivitySource.StartActivity(
                    diagnostic.Name,
                    diagnostic.Kind is ApplicationDiagnosticKind.Input or ApplicationDiagnosticKind.Output
                        ? ActivityKind.Producer
                        : ActivityKind.Internal);
                if (activity is not null)
                {
                    activity.SetTag("flow.trace_id", message.TraceId.Value);
                    activity.SetTag("flow.message_id", message.MessageId.Value);
                    activity.SetTag("flow.diagnostic.kind", diagnostic.Kind.ToString());
                    activity.SetTag("flow.diagnostic.subject", diagnostic.Subject);
                    activity.SetTag("flow.diagnostic.duration_ms", diagnostic.Duration?.TotalMilliseconds);
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
        FlowMessage<ApplicationSystemEvent> message,
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
        ApplicationDiagnostic diagnostic,
        FlowMessage<ApplicationDiagnostic> message)
    {
        var tags = new TagList
        {
            { "flow.diagnostic.name", diagnostic.Name },
            { "flow.diagnostic.kind", diagnostic.Kind.ToString() },
            { "flow.diagnostic.level", diagnostic.Level.ToString() },
            { "flow.diagnostic.subject", diagnostic.Subject },
            { "flow.trace_id", message.TraceId.Value }
        };
        if (!string.IsNullOrWhiteSpace(diagnostic.Unit))
            tags.Add("flow.diagnostic.unit", diagnostic.Unit);
        return tags;
    }

    private static bool ShouldCreateActivity(ApplicationDiagnosticKind kind)
        => kind is ApplicationDiagnosticKind.Input or
            ApplicationDiagnosticKind.Output or
            ApplicationDiagnosticKind.Timing or
            ApplicationDiagnosticKind.Trace;

    private static LogLevel MapLevel(ApplicationDiagnosticLevel level)
        => level switch
        {
            ApplicationDiagnosticLevel.Trace => LogLevel.Trace,
            ApplicationDiagnosticLevel.Debug => LogLevel.Debug,
            ApplicationDiagnosticLevel.Information => LogLevel.Information,
            ApplicationDiagnosticLevel.Warning => LogLevel.Warning,
            ApplicationDiagnosticLevel.Error => LogLevel.Error,
            ApplicationDiagnosticLevel.Critical => LogLevel.Critical,
            _ => LogLevel.None
        };
}
