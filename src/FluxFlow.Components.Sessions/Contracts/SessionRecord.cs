namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionRecord
{
    public required string SessionId { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Type { get; init; }
    public string? Name { get; init; }
    public object? Payload { get; init; }
    public string? ContentType { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
}
