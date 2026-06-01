using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.StateCompositionSample;

internal static class SampleNodeTypes
{
    public static readonly NodeType StateSink = new("sample.state-sink");
    public static readonly NodeType CounterSink = new("sample.counter-sink");
}

internal static class SampleComponentRegistration
{
    public static RuntimeNodeFactoryRegistry RegisterSampleComponents(
        this RuntimeNodeFactoryRegistry registry,
        SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(capture);

        return registry.Register(new SampleComponentModule(capture));
    }
}

internal sealed class SampleComponentModule : IFlowNodeModule
{
    public SampleComponentModule(SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        Registrations =
        [
            new FlowNodeRegistration(
                SampleNodeTypes.StateSink,
                context => StateSinkNode.Create(context, capture)),
            new FlowNodeRegistration(
                SampleNodeTypes.CounterSink,
                context => CounterSinkNode.Create(context, capture))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}

internal sealed class StateSinkNode(SampleCapture capture) : SinkFlowNode<StateReducerResult>(
    new ExecutionDataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context, SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capture);

        var node = new StateSinkNode(capture);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        StateReducerResult input,
        CancellationToken cancellationToken)
    {
        capture.Add(input);
        return ValueTask.CompletedTask;
    }
}

internal sealed class CounterSinkNode(SampleCapture capture) : SinkFlowNode<FlowCounterSnapshot>(
    new ExecutionDataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context, SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capture);

        var node = new CounterSinkNode(capture);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        FlowCounterSnapshot input,
        CancellationToken cancellationToken)
    {
        capture.Add(input);
        return ValueTask.CompletedTask;
    }
}
