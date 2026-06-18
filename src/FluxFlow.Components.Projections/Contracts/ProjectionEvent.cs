namespace FluxFlow.Components.Projections.Contracts;

/// <summary>
/// The standalone domain event the projection node observes. A self-contained copy
/// of the shape a host event carries (type, source, subject, status, channel, an
/// optional payload preview, and an attribute bag) with no engine dependency — feed
/// the node a <c>FlowMessage&lt;ProjectionEvent&gt;</c> and it folds matching events
/// into rolling <see cref="EventProjectionSnapshot"/> values.
/// </summary>
public sealed record ProjectionEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Type { get; init; }
    public required string Source { get; init; }
    public string? SourceNodeId { get; init; }
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
