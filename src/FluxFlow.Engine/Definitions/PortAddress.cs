namespace FluxFlow.Engine.Definitions;

public sealed record PortAddress(string Scope, NodeName Node, PortName Port, string SubPath = "")
{
    public IReadOnlyList<string> PathSegments =>
        SubPath.Length == 0 ? [] : SubPath.Split('.');

    public NodeAddress NodeAddress => new(Scope, Node);

    /// <summary>
    /// Parses a fully-qualified address: scope.node.port[.path...]
    /// </summary>
    public static PortAddress Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Port address cannot be empty.");

        var parts = value.Split('.');
        if (parts.Length < 3)
            throw new FormatException(
                $"Port address '{value}' must have at least 3 segments (scope.node.port[.path...]).");

        if (string.IsNullOrWhiteSpace(parts[0]))
            throw new FormatException($"Port address '{value}' scope cannot be empty.");

        return new PortAddress(
            parts[0],
            new NodeName(parts[1]),
            new PortName(parts[2]),
            parts.Length > 3 ? string.Join('.', parts[3..]) : string.Empty);
    }

    /// <summary>
    /// Parses a 2-segment short form (node.port) by prepending the workflow scope,
    /// or passes through an already fully-qualified address (3+ segments) unchanged.
    /// </summary>
    public static PortAddress ExpandAndParse(string value, string workflowName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Port address cannot be empty.");

        var parts = value.Split('.');
        return parts.Length switch
        {
            >= 3 => Parse(value),
            2 => new PortAddress(workflowName, new NodeName(parts[0]), new PortName(parts[1])),
            _ => throw new FormatException(
                $"Port address '{value}' must have at least 2 segments (node.port or scope.node.port[.path...]).")
        };
    }

    public override string ToString() =>
        SubPath.Length == 0
            ? $"{Scope}.{Node}.{Port}"
            : $"{Scope}.{Node}.{Port}.{SubPath}";
}
