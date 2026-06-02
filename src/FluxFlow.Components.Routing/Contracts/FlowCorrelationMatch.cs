namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowCorrelationMatch<TInput>
{
    public required string Key { get; init; }
    public required TInput Request { get; init; }
    public required TInput Response { get; init; }
    public required DateTimeOffset RequestReceivedAt { get; init; }
    public required DateTimeOffset ResponseReceivedAt { get; init; }
    public DateTimeOffset MatchedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Elapsed { get; init; }
}
