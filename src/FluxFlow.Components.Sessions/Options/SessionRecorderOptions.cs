namespace FluxFlow.Components.Sessions.Options;

public sealed record SessionRecorderOptions
{
    public string? Store { get; init; }
    public string? SessionId { get; init; }
    public string? Name { get; init; }
    public string? Notes { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
    public int BoundedCapacity { get; init; } = 128;
}
