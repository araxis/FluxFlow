namespace FluxFlow.Engine.Ports;

public sealed record ApplicationPortRevisionInfo
{
    public required long Sequence { get; init; }

    public required string RevisionId { get; init; }

    public required DateTimeOffset ActivatedAt { get; init; }
}
