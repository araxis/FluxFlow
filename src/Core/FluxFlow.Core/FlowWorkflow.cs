using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>A naming and ownership scope; workflows do not schedule messages.</summary>
public sealed class FlowWorkflow
{
    private readonly FlowTopology _topology;
    internal FlowWorkflow(FlowTopology topology, string name) => (_topology, Name) = (topology, name);
    public string Name { get; }

    /// <summary>Successful addition transfers disposal ownership unless explicitly borrowed.</summary>
    public FlowComponent Add(string name, IFlowComponent component, ComponentOwnership ownership = ComponentOwnership.Owned)
        => _topology.AddComponent(Name, name, component, ownership);

    internal FlowComponent Add(string name, IFlowComponent component, IFlowNode node)
        => _topology.AddComponent(Name, name, component, ComponentOwnership.Owned, node);
}
