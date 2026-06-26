namespace FluxFlow.Composition;

public sealed record NodeReference
{
    private string? _workflow;
    private string _node = string.Empty;

    public string? Workflow
    {
        get => _workflow;
        init => _workflow = NormalizeOptional(value);
    }

    public required string Node
    {
        get => _node;
        init => _node = NormalizeRequired(value);
    }

    public static NodeReference Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("Node references cannot contain empty segments.");

        return parts.Length switch
        {
            1 => new NodeReference { Node = parts[0] },
            2 => new NodeReference { Workflow = parts[0], Node = parts[1] },
            _ => throw new FormatException("Node references must use 'node' or 'workflow.node'.")
        };
    }

    public override string ToString()
        => string.IsNullOrWhiteSpace(Workflow) ? Node : $"{Workflow}.{Node}";

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequired(string? value)
        => value?.Trim() ?? string.Empty;
}
