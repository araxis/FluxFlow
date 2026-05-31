namespace FluxFlow.Engine.Definitions;

public readonly record struct NodeType
{
    public NodeType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Flow node type cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
