using System.Collections.ObjectModel;
using FluxFlow.Nodes;

namespace FluxFlow.Core;

public enum ComponentOwnership
{
    Owned,
    Borrowed
}

/// <summary>A named view of an existing instance, not an activation descriptor.</summary>
public sealed class FlowComponent
{
    internal FlowComponent(FlowTopology topology, string address, IFlowComponent instance, ComponentOwnership ownership,
        IFlowNode node)
    {
        Address = address;
        Instance = instance;
        Node = node;
        Ownership = ownership;
        var ports = instance.Ports ?? throw new ArgumentException("A component must declare its ports.", nameof(instance));
        Inputs = new ReadOnlyDictionary<string, FlowInputEndpoint>(ports.Inputs.Concat(ports.Controls).ToDictionary(
            p => p.Key, p => new FlowInputEndpoint(topology, this, p.Key, p.Value, ports.Controls.ContainsKey(p.Key)), StringComparer.Ordinal));
        Outputs = new ReadOnlyDictionary<string, FlowOutputEndpoint>(ports.Outputs.ToDictionary(
            p => p.Key, p => new FlowOutputEndpoint(topology, this, p.Key, p.Value), StringComparer.Ordinal));
    }

    public string Address { get; }
    public IFlowComponent Instance { get; }
    internal IFlowNode Node { get; }
    public ComponentOwnership Ownership { get; }
    public IReadOnlyDictionary<string, FlowInputEndpoint> Inputs { get; }
    public IReadOnlyDictionary<string, FlowOutputEndpoint> Outputs { get; }
    public FlowInputEndpoint Input => Inputs[FlowPorts.InputName];
    public FlowOutputEndpoint Output => Outputs[FlowPorts.OutputName];
}
