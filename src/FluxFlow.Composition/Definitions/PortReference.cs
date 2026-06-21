namespace FluxFlow.Composition;

public sealed record PortReference
{
    public string? Workflow { get; init; }

    public required string Node { get; init; }

    public required string Port { get; init; }

    public static PortReference Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
}
