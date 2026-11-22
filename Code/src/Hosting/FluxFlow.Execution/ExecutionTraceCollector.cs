using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
using FluxFlow.Environments;

namespace FluxFlow.Execution;

public sealed class ExecutionTraceCollector : IDisposable
{
    private readonly AsyncLocal<ExecutionTraceBuffer?> _current = new();
    private readonly AsyncLocal<ApplicationInstance?> _instance = new();
    private readonly TracerProvider _provider;

    public ExecutionTraceCollector()
    {
        var destinations = new ConditionalWeakTable<Activity, ExecutionTraceBuffer>();
        _provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("FluxFlow.Core")
            // Interactive local execution records spans even when an incoming parent was not sampled.
            .SetSampler(new AlwaysOnSampler())
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("FluxFlow.Execution"))
            .AddProcessor(new RunCapture(_current, _instance, destinations))
            .AddProcessor(new Processor(destinations))
            .Build();
    }

    internal IDisposable Capture(ExecutionTraceBuffer buffer, ApplicationInstance? instance = null)
    {
        var scope = new Scope(_current, _current.Value, _instance, _instance.Value);
        _current.Value = buffer;
        _instance.Value = instance;
        return scope;
    }

    public void Dispose() => _provider.Dispose();

    private sealed class Scope(AsyncLocal<ExecutionTraceBuffer?> current, ExecutionTraceBuffer? previous,
        AsyncLocal<ApplicationInstance?> instance, ApplicationInstance? previousInstance) : IDisposable
    {
        public void Dispose() { current.Value = previous; instance.Value = previousInstance; }
    }

    private sealed class RunCapture(AsyncLocal<ExecutionTraceBuffer?> current,
        AsyncLocal<ApplicationInstance?> instance,
        ConditionalWeakTable<Activity, ExecutionTraceBuffer> destinations) : BaseProcessor<Activity>
    {
        public override void OnStart(Activity activity)
        {
            if (current.Value is { } buffer)
            {
                destinations.Add(activity, buffer);
                if (buffer.RunId is { } id) activity.SetTag("flow.run.id", id.ToString());
                if (instance.Value is { } identity)
                    foreach (var attribute in WorkflowExecution.InstanceAttributes(identity)) activity.SetTag(attribute.Key, attribute.Value);
            }
        }
    }

    private sealed class Processor : SimpleActivityExportProcessor
    {
        private readonly ExecutionOtlpCapture _capture;
        private readonly ConditionalWeakTable<Activity, ExecutionTraceBuffer> _destinations;
        public Processor(ConditionalWeakTable<Activity, ExecutionTraceBuffer> destinations) : this(destinations, new ExecutionOtlpCapture()) { }
        private Processor(ConditionalWeakTable<Activity, ExecutionTraceBuffer> destinations, ExecutionOtlpCapture capture)
            : base(new OtlpTraceExporter(capture.Options("traces"))) => (_destinations, _capture) = (destinations, capture);

        public override void OnEnd(Activity activity)
        {
            // Finish in the starting run even if the ambient context changed while the span was open.
            if (!_destinations.TryGetValue(activity, out var buffer)) return;
            _destinations.Remove(activity);
            using var capture = _capture.Capture(buffer.Append);
            base.OnEnd(activity);
        }
    }
}
