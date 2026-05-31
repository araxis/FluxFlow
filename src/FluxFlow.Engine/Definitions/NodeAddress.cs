namespace FluxFlow.Engine.Definitions;

public sealed record NodeAddress(string Scope, NodeName Node)
{
    public static NodeAddress Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Node address cannot be empty.");

        var parts = value.Split('.');
        if (parts.Length != 2)
            throw new FormatException($"Node address '{value}' must have exactly 2 segments (scope.node).");

        if (string.IsNullOrWhiteSpace(parts[0]))
            throw new FormatException($"Node address '{value}' scope cannot be empty.");

        return new NodeAddress(parts[0], new NodeName(parts[1]));
    }

    public PortAddress Port(PortName port) => new(Scope, Node, port);

    public override string ToString() => $"{Scope}.{Node}";
}
