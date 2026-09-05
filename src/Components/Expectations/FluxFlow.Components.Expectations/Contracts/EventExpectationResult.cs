using FluxFlow.Components.Projections.Contracts;

namespace FluxFlow.Components.Expectations.Contracts;

public sealed record EventExpectationResult
{
    public required DateTimeOffset EvaluatedAt { get; init; }
    public string? Name { get; init; }
    public required EventExpectationResultKind Kind { get; init; }
    public required bool Satisfied { get; init; }
    public required bool Matched { get; init; }
    public required bool TimedOut { get; init; }
    public EventSummary? MatchedEvent { get; init; }
    public IReadOnlyList<EventSummary> ObservedEvents { get; init; } = [];
    public EventFilter Filter { get; init; } = new();
    public string? Reason { get; init; }
}
