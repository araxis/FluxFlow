using FluxFlow.Composition.Model;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class CompositionRuntimeNode
{
    internal CompositionRuntimeNode(
        RuntimeNodeKey key,
        ComponentDefinition component,
        ComposedNode descriptor)
    {
        Key = key;
        Component = component;
        Descriptor = descriptor;
    }

    internal RuntimeNodeKey Key { get; }

    public string WorkflowName => Key.WorkflowName;

    public string ComponentName => Key.ComponentName;

    public ComponentDefinition Component { get; }

    public ComposedNode Descriptor { get; }

    public IFlowNode Node => Descriptor.Node;
}
