using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed class FlowNodeRegistration : IFlowNodeRegistration
{
    private readonly RuntimeNodeFactory _factory;

    public FlowNodeRegistration(
        NodeType type,
        RuntimeNodeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Type = type;
        _factory = factory;
    }

    public FlowNodeRegistration(
        NodeType type,
        Func<NodeAddress, NodeDefinition, RuntimeNode> factory)
        : this(type, CreateFactory(factory))
    {
    }

    public NodeType Type { get; }

    public RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _factory(context);
    }

    private static RuntimeNodeFactory CreateFactory(
        Func<NodeAddress, NodeDefinition, RuntimeNode> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return context => factory(context.Address, context.Definition);
    }
}
