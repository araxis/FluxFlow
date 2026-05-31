using FluxFlow.Engine.Core;

namespace FluxFlow.Engine.Components;

public sealed record FlowEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Type { get; init; }
    public required string Source { get; init; }
    public FlowNodeId? SourceNodeId { get; init; }
    public string? Subject { get; init; }
    public string? Status { get; init; }
    public string? Channel { get; init; }
    public int? PayloadBytes { get; init; }
    public string? PayloadPreview { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string? GetAttribute(string name)
        => Attributes.TryGetValue(name, out var value) ? value : null;
}
