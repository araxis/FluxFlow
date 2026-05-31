namespace FluxFlow.Engine.Core;

public readonly record struct FlowNodeId(Guid Value)
{
    public static FlowNodeId New() => new(Guid.NewGuid());
    public static FlowNodeId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
