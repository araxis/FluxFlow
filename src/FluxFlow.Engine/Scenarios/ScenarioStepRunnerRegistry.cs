namespace FluxFlow.Engine.Scenarios;

public sealed class ScenarioStepRunnerRegistry
{
    private readonly Dictionary<string, IScenarioStepRunner> _runners = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, IScenarioStepRunner> Runners => _runners;

    public static ScenarioStepRunnerRegistry CreateEventExpectationOnly()
        => new ScenarioStepRunnerRegistry()
            .Register(new ExpectEventScenarioStepRunner());

    public ScenarioStepRunnerRegistry Register(IScenarioStepRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (string.IsNullOrWhiteSpace(runner.Type))
        {
            throw new ArgumentException("Scenario step runner type cannot be empty.", nameof(runner));
        }

        _runners[runner.Type] = runner;
        return this;
    }

    public bool TryGet(string type, out IScenarioStepRunner runner)
        => _runners.TryGetValue(type, out runner!);
}
