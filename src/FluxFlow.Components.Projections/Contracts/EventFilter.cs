namespace FluxFlow.Components.Projections.Contracts;

public sealed record EventFilter
{
    public string? Type { get; init; }
    public string? TypePrefix { get; init; }
    public string? SubjectPrefix { get; init; }
    public string? ChannelPrefix { get; init; }
    public string? ExcludedSubjectPrefix { get; init; }
    public string? ExcludedChannelPrefix { get; init; }
    public string? Status { get; init; }
    public string? Source { get; init; }
    public string? SourceNodeId { get; init; }
    public string? ComponentId { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
