namespace FluxFlow.Components.Sources.Contracts;

/// <summary>
/// Describes one value emitted by a numeric sequence source.
/// </summary>
public sealed record SequenceItem(
    string Name,
    long Sequence,
    long Start,
    long Step,
    DateTimeOffset Timestamp,
    long Value);
