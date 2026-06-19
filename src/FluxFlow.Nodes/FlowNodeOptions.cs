namespace FluxFlow.Nodes;

/// <summary>
/// Shape of a node's input pump. Inputs are a bounded buffer (so a node applies
/// backpressure on its own intake); outputs are broadcast (no backpressure).
/// </summary>
public sealed record FlowNodeOptions
{
    public int InputCapacity { get; init; } = 128;

    public int MaxDegreeOfParallelism { get; init; } = 1;
}
