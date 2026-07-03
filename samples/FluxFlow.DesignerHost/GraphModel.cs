using System.Text.Json;

namespace FluxFlow.DesignerHost;

/// <summary>
/// Host-local view of one editable workflow graph. Option values stay as raw
/// <see cref="JsonElement"/> data so persistence keeps full fidelity with
/// composition definitions; display concerns come from the metadata catalog, not
/// from this model. Layout is host-only state and never enters a definition.
/// </summary>
public sealed record GraphModel
{
    public required string WorkflowName { get; init; }
    public IReadOnlyList<GraphNodeModel> Nodes { get; init; } = [];
    public IReadOnlyList<GraphLinkModel> Links { get; init; } = [];
}

public sealed record GraphNodeModel
{
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Options { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Resources { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Canvas position; host-only, excluded from definition mapping.</summary>
    public GraphLayoutModel Layout { get; init; } = new();
}

public sealed record GraphLayoutModel
{
    public double X { get; init; }
    public double Y { get; init; }
}

/// <summary>
/// One port link. The workflow segments stay null for workflow-local links; a
/// non-null value preserves cross-workflow references a definition may contain.
/// </summary>
public sealed record GraphLinkModel
{
    public required string FromNode { get; init; }
    public required string FromPort { get; init; }
    public required string ToNode { get; init; }
    public required string ToPort { get; init; }
    public string? FromWorkflow { get; init; }
    public string? ToWorkflow { get; init; }
}
