namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalQuery
{
    public string? Type { get; init; }
    public string? TypePrefix { get; init; }
    public string? Status { get; init; }
    public string? Source { get; init; }
    public string? WorkflowId { get; init; }
    public string? WorkflowName { get; init; }
    public string? NodeId { get; init; }
    public string? ComponentId { get; init; }
    public string? SubjectPrefix { get; init; }
    public string? ChannelPrefix { get; init; }
    public string? ExcludedSubjectPrefix { get; init; }
    public string? ExcludedChannelPrefix { get; init; }
    public string? Severity { get; init; }
    public string? Level { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Offset { get; init; }
    public int? Limit { get; init; }
}
