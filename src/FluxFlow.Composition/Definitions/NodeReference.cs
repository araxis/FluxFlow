namespace FluxFlow.Composition;

public sealed record NodeReference
{
    public string? Workflow { get; init; }

    public required string Node { get; init; }

    public static NodeReference Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => new NodeReference { Node = parts[0] },
            2 => new NodeReference { Workflow = parts[0], Node = parts[1] },
            _ => throw new FormatException("Node references must use 'node' or 'workflow.node'.")
        };
    }

    public override string ToString()
        => string.IsNullOrWhiteSpace(Workflow) ? Node : $"{Workflow}.{Node}";
}
