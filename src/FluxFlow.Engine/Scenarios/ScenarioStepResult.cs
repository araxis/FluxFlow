using FluxFlow.Engine.Components;

namespace FluxFlow.Engine.Scenarios;

public sealed record ScenarioStepResult
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required ScenarioStepRunStatus Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset FinishedAt { get; init; }
    public string? Message { get; init; }
    public FlowEvent? MatchedEvent { get; init; }
    public int? MatchedEventIndex { get; init; }
    public int NextEventOffset { get; init; }

    public bool IsSuccess => Status == ScenarioStepRunStatus.Passed;
}
