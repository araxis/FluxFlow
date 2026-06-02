namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionQueryRequest
{
    public string? Name { get; init; }
    public string? NamePrefix { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
    public DateTimeOffset? StartedFrom { get; init; }
    public DateTimeOffset? StartedTo { get; init; }
    public DateTimeOffset? EndedFrom { get; init; }
    public DateTimeOffset? EndedTo { get; init; }
    public bool? IncludeActive { get; init; }
    public bool? IncludeCompleted { get; init; }
    public int? Limit { get; init; }
    public string? CorrelationId { get; init; }
}
