namespace FluxFlow.Components.Sources.Contracts;

public sealed record SourceSequenceItem
{
    public required string Name { get; init; }
    public long Sequence { get; init; }
    public long Value { get; init; }
    public long Start { get; init; }
    public long Step { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
