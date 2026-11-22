using FluxFlow.Core.Messaging;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FluxFlow.Core.Nodes;

/// <summary>Runtime instruments. Hosts choose listeners, aggregation and export.</summary>
public static class FlowRuntimeMetrics
{
    public const string MeterName = "FluxFlow.Core";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Completed = Meter.CreateCounter<long>("fluxflow.processing.completed", "{operation}");
    private static readonly UpDownCounter<long> Active = Meter.CreateUpDownCounter<long>("fluxflow.processing.active", "{operation}");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("fluxflow.processing.duration", "s");
    private static readonly Counter<long> Exceptions = Meter.CreateCounter<long>("fluxflow.exceptions", "{exception}");

    internal static Measurement Start(object component)
    {
        try
        {
            if (!Completed.Enabled && !Active.Enabled && !Duration.Enabled) return default;
            var tags = FlowDiagnostics.ComponentTags(component);
            try { Active.Add(1, tags); } catch { }
            return new(tags, Stopwatch.GetTimestamp());
        }
        catch { return default; }
    }

    internal static void Exception(object component, string operation, Exception exception)
    {
        try
        {
            if (!Exceptions.Enabled) return;
            var tags = FlowDiagnostics.ComponentTags(component);
            tags.Add("flow.operation", operation);
            tags.Add("error.type", exception.GetType().FullName);
            Exceptions.Add(1, tags);
        }
        catch { /* Metrics observers must not affect processing. */ }
    }

    internal readonly record struct Measurement(TagList Tags, long Started)
    {
        internal void Stop()
        {
            if (Tags.Count == 0) return;
            // Independent calls: one failing listener must not suppress the other instruments.
            try { Active.Add(-1, Tags); } catch { }
            try { Completed.Add(1, Tags); } catch { }
            try { Duration.Record(Stopwatch.GetElapsedTime(Started).TotalSeconds, Tags); } catch { }
        }
    }
}
