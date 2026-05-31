using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed record RuntimeNode(
    NodeAddress Address,
    IFlowNode Node,
    IReadOnlyList<InputPort> Inputs,
    IReadOnlyList<OutputPort> Outputs,
    int Phase = 0)
{
    public InputPort? FindInput(PortName port)
        => Inputs.FirstOrDefault(p => p.Address.Port == port);

    public OutputPort? FindOutput(PortName port)
        => Outputs.FirstOrDefault(p => p.Address.Port == port);

    public static RuntimeNode Create(
        NodeAddress address,
        IFlowNode node,
        IEnumerable<InputPort>? inputs = null,
        IEnumerable<OutputPort>? outputs = null,
        int phase = 0)
        => new(
            address,
            node,
            (inputs ?? []).ToArray(),
            (outputs ?? []).ToArray(),
            phase);
}
