using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Scenarios;

public sealed record ScenarioStepRunContext
{
    public required string ScenarioName { get; init; }
    public required string StepName { get; init; }
    public required ScenarioStepDefinition Step { get; init; }
    public required ScenarioEventJournal Events { get; init; }
    public required ScenarioRunLifetime Lifetime { get; init; }
    public ScenarioStepServices Services { get; init; } = ScenarioStepServices.Empty;
    public int EventOffset { get; init; }
    public IReadOnlySet<int> ConsumedEventIndexes { get; init; } = new HashSet<int>();
}
