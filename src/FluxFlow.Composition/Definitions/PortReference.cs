namespace FluxFlow.Composition;

public sealed record PortReference
{
    private string? _workflow;
    private string _node = string.Empty;
    private string _port = string.Empty;

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

    public required string Port
    {
        get => _port;
        init => _port = NormalizeRequired(value);
    }

    public static PortReference Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("Port references cannot contain empty segments.");

        return parts.Length switch
        {
            2 => new PortReference { Node = parts[0], Port = parts[1] },
            3 => new PortReference { Workflow = parts[0], Node = parts[1], Port = parts[2] },
            _ => throw new FormatException("Port references must use 'node.port' or 'workflow.node.port'.")
        };
    }

    public PortReference ResolveWorkflow(string workflowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        return string.IsNullOrWhiteSpace(Workflow)
            ? this with { Workflow = workflowName }
            : this;
    }

    public override string ToString()
        => string.IsNullOrWhiteSpace(Workflow) ? $"{Node}.{Port}" : $"{Workflow}.{Node}.{Port}";

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequired(string? value)
        => value?.Trim() ?? string.Empty;
}
