using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>Correlates typed request and response values into matched or timed-out outcomes.</summary>
public class CorrelationNode<TInput> : IFlowNode
{
    private readonly CorrelationNodeRuntime<TInput> _inner;

    public CorrelationNode(
        CorrelationRoutingOptions options,
        Func<TInput, string?> keySelector,
        Func<TInput, string?> sideSelector,
        string? engineName = null,
        TimeProvider? clock = null)
    {
        _inner = new CorrelationNodeRuntime<TInput>(
            options,
            keySelector,
            sideSelector,
            engineName,
            clock);
    }

    public ITargetBlock<FlowMessage<TInput>> Input => _inner.Input;

    public ISourceBlock<FlowMessage<FlowCorrelationOutcome<TInput>>> Output => _inner.Output;

    public ISourceBlock<FlowEvent> Events => _inner.Events;

    public Task Completion => _inner.Completion;

    public void Complete() => _inner.Complete();

    public void Fault(Exception exception) => _inner.Fault(exception);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>Schema-less JSON correlation node for configuration-driven workflows.</summary>
public sealed class JsonCorrelationNode : CorrelationNode<JsonElement>
{
    public JsonCorrelationNode(
        CorrelationRoutingOptions options,
        Func<JsonElement, string?> keySelector,
        Func<JsonElement, string?> sideSelector,
        string? engineName = null,
        TimeProvider? clock = null)
        : base(options, keySelector, sideSelector, engineName, clock)
    {
    }
}
