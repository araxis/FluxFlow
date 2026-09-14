using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>A naming and ownership scope; workflows do not schedule messages.</summary>
public sealed class FlowWorkflow
{
    private readonly FlowTopology _topology;
    private readonly FlowApplication _application;
    internal FlowWorkflow(FlowTopology topology, string name, FlowApplication application)
        => (_topology, Name, _application) = (topology, name, application);
    public string Name { get; }

    internal FlowApplication Application => _application;

    /// <summary>Successful addition transfers disposal ownership unless explicitly borrowed.</summary>
    public FlowComponent Add(string name, IFlowComponent component, ComponentOwnership ownership = ComponentOwnership.Owned)
        => _topology.AddComponent(Name, name, component, ownership);

    internal FlowComponent Add(string name, IFlowComponent component, IFlowNode node)
        => _topology.AddComponent(Name, name, component, ComponentOwnership.Owned, node);
}
