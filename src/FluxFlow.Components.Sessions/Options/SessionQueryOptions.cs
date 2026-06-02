namespace FluxFlow.Components.Sessions.Options;

public sealed record SessionQueryOptions
{
    public string? Store { get; init; }
    public string? Name { get; init; }
    public string? NamePrefix { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
    public bool IncludeActive { get; init; } = true;
    public bool IncludeCompleted { get; init; } = true;
    public int Limit { get; init; } = 100;
    public bool EmitSessionsInResult { get; init; } = true;
    public bool EmitSessionOutputs { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
