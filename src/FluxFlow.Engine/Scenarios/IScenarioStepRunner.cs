namespace FluxFlow.Engine.Scenarios;

public interface IScenarioStepRunner
{
    string Type { get; }

    Task<ScenarioStepResult> RunAsync(
        ScenarioStepRunContext context,
        CancellationToken cancellationToken = default);
}
