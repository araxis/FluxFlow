namespace FluxFlow.Engine.Scenarios;

public sealed record ScenarioRunResult
{
    public required string Name { get; init; }
    public required ScenarioRunStatus Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset FinishedAt { get; init; }
    public IReadOnlyList<ScenarioStepResult> Steps { get; init; } = [];

    public bool IsSuccess => Status == ScenarioRunStatus.Passed;
}
