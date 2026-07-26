using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>Groups typed input values into count- or time-bounded windows.</summary>
public class WindowNode<TInput> : IFlowNode
{
    private readonly WindowNodeRuntime<TInput> _inner;

    public WindowNode(WindowRoutingOptions options, TimeProvider? clock = null)
    {
        _inner = new WindowNodeRuntime<TInput>(options, clock);
    }

    public ITargetBlock<FlowMessage<TInput>> Input => _inner.Input;

    public ISourceBlock<FlowMessage<FlowWindow<TInput>>> Output => _inner.Output;

    public ISourceBlock<FlowEvent> Events => _inner.Events;

    public Task Completion => _inner.Completion;

    public void Complete() => _inner.Complete();

    public void Fault(Exception exception) => _inner.Fault(exception);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>Schema-less JSON window node for configuration-driven workflows.</summary>
public sealed class JsonWindowNode : WindowNode<JsonElement>
{
    public JsonWindowNode(WindowRoutingOptions options, TimeProvider? clock = null)
        : base(options, clock)
    {
    }
}
