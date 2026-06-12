using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed class RuntimeNodeFactoryRegistry
{
    private readonly Dictionary<NodeType, RuntimeNodeFactory> _factories = [];
    private readonly object _gate = new();

    public IReadOnlyDictionary<NodeType, RuntimeNodeFactory> Factories
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<NodeType, RuntimeNodeFactory>(_factories);
            }
        }
    }

    public RuntimeNodeFactoryRegistry Register(NodeType type, RuntimeNodeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            if (!_factories.TryAdd(type, factory))
            {
                throw new InvalidOperationException($"A flow node factory is already registered for '{type}'.");
            }
        }

        return this;
    }

    public RuntimeNodeFactoryRegistry Register(
        NodeType type,
        Func<NodeAddress, NodeDefinition, RuntimeNode> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return Register(type, context => factory(context.Address, context.Definition));
    }

    public bool TryGetFactory(NodeType type, out RuntimeNodeFactory factory)
    {
        lock (_gate)
        {
            return _factories.TryGetValue(type, out factory!);
        }
    }
}
