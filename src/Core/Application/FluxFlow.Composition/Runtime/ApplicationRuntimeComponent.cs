using FluxFlow.Composition.Model;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class ApplicationRuntimeComponent
{
    internal ApplicationRuntimeComponent(
        RuntimeNodeKey key,
        ComponentDefinition component,
        ComponentInstance descriptor)
    {
        Key = key;
        Component = component;
        Descriptor = descriptor;
    }

    internal RuntimeNodeKey Key { get; }

    public string WorkflowName => Key.WorkflowName;

    public string ComponentName => Key.ComponentName;

    public ComponentDefinition Component { get; }

    public ComponentInstance Descriptor { get; }

    public IFlowNode Node => Descriptor.Node;
}
