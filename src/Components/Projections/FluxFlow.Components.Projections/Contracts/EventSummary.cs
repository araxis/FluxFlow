namespace FluxFlow.Components.Projections.Contracts;

public sealed record EventSummary
{
    private IReadOnlyDictionary<string, string> _attributes =
        ProjectionContractNormalization.CopyAttributes(null);

    public required DateTimeOffset Timestamp { get; init; }
    public required string Type { get; init; }
    public required string Source { get; init; }
    public string? SourceNodeId { get; init; }
    public string? Subject { get; init; }
    public string? Status { get; init; }
    public string? Channel { get; init; }
    public int? PayloadBytes { get; init; }
    public string? PayloadPreview { get; init; }
    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = ProjectionContractNormalization.CopyAttributes(value);
    }
}
