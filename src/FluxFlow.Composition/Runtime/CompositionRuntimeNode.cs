using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class CompositionRuntimeNode
{
    internal CompositionRuntimeNode(
        RuntimeNodeKey key,
        NodeDefinition definition,
        ComposedNode descriptor)
    {
        Key = key;
        Definition = definition;
        Descriptor = descriptor;
    }

    internal RuntimeNodeKey Key { get; }

    public string WorkflowName => Key.WorkflowName;

    public string NodeName => Key.NodeName;

    public NodeDefinition Definition { get; }

    public ComposedNode Descriptor { get; }

    public IFlowNode Node => Descriptor.Node;
}
