using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>Registers instances and resolves their real ports; execution belongs to Core.</summary>
internal sealed class FlowGraphBuilder
{
    private readonly FlowApplication _application = new();
    private readonly FlowWorkflow _workflow;
    private readonly Dictionary<IFlowComponent, FlowComponent> _components =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ISourceBlock<FlowMessage>, FlowOutputEndpoint> _outputs =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<Action<FlowEvent>> _eventHandlers = [];
    private bool _built;

    internal FlowGraphBuilder() => _workflow = _application.AddWorkflow("main");

    internal FlowComponent Register(IFlowComponent instance, string? name)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(instance);
        if (_components.TryGetValue(instance, out var existing))
        {
            if (name is not null && existing.Address != $"main.{name}")
                throw new ArgumentException("An attached instance already has a different name.", nameof(name));
            return existing;
        }

        var component = _workflow.Add(name ?? $"node{_components.Count + 1:D4}", instance);
        _components.Add(instance, component);
        foreach (var (portName, block) in instance.Ports.Outputs)
            _outputs.Add(block, component.Outputs[portName]);
        return component;
    }

    internal FlowOutputEndpoint GetOutput(ISourceBlock<FlowMessage> port)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(port);
        return _outputs.TryGetValue(port, out var output)
            ? output
            : throw new ArgumentException("The branch must use a declared output of an attached component.", nameof(port));
    }

    internal void Link(FlowOutputEndpoint source, FlowInputEndpoint target)
    {
        EnsureMutable();
        source.LinkTo(target);
    }

    internal void DiscardUnmatched(FlowOutputEndpoint output)
    {
        EnsureMutable();
        output.DiscardUnmatched();
    }

    internal void MarkTerminal(FlowComponent component)
    {
        EnsureMutable();
        foreach (var output in component.Outputs.Values)
            output.DiscardUnmatched();
    }

    internal void OnEvent(Action<FlowEvent> handler)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(handler);
        _eventHandlers.Add(handler);
    }

    internal FlowGraph Build()
    {
        EnsureMutable();
        _application.Validate();
        _built = true;
        var events = _components.Keys.Select(component => component.Ports.Events)
            .OfType<ISourceBlock<FlowMessage>>().ToArray();
        var graph = new FlowGraph(_application, events);
        foreach (var handler in _eventHandlers)
            graph.OnEvent(handler);
        return graph;
    }

    private void EnsureMutable()
    {
        if (_built) throw new InvalidOperationException("The pipeline has already been built.");
    }
}
