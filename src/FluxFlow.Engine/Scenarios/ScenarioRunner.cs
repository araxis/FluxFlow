using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Scenarios;

public sealed class ScenarioRunner(ScenarioStepRunnerRegistry registry)
{
    private readonly ScenarioStepRunnerRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public async Task<ScenarioRunResult> RunAsync(
        string name,
        ScenarioDefinition scenario,
        ISourceBlock<FlowEvent> events,
        CancellationToken cancellationToken = default)
        => await RunAsync(
            name,
            scenario,
            events,
            ScenarioStepServices.Empty,
            cancellationToken).ConfigureAwait(false);

    public async Task<ScenarioRunResult> RunAsync(
        string name,
        ScenarioDefinition scenario,
        ISourceBlock<FlowEvent> events,
        ScenarioStepServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(services);

        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<ScenarioStepResult>();
        var eventOffset = 0;
        var consumedEventIndexes = new HashSet<int>();

        var lifetime = new ScenarioRunLifetime();
        using var journal = new ScenarioEventJournal(events, startedAt);

        foreach (var step in scenario.Steps)
        {
            var result = await RunStepAsync(
                name,
                step.Key,
                step.Value,
                journal,
                lifetime,
                services,
                eventOffset,
                consumedEventIndexes,
                cancellationToken).ConfigureAwait(false);

            results.Add(result);
            if (result.MatchedEventIndex is { } matchedEventIndex)
            {
                consumedEventIndexes.Add(matchedEventIndex);
            }

            eventOffset = result.NextEventOffset;

            if (!result.IsSuccess)
            {
                break;
            }
        }

        if (await DisposeLifetimeAsync(lifetime, eventOffset).ConfigureAwait(false) is { } cleanupResult)
        {
            results.Add(cleanupResult);
        }

        var finishedAt = DateTimeOffset.UtcNow;
        return new ScenarioRunResult
        {
            Name = name,
            Status = ResolveStatus(results),
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Steps = results
        };
    }

    private async Task<ScenarioStepResult> RunStepAsync(
        string scenarioName,
        string stepName,
        ScenarioStepDefinition step,
        ScenarioEventJournal events,
        ScenarioRunLifetime lifetime,
        ScenarioStepServices services,
        int eventOffset,
        IReadOnlySet<int> consumedEventIndexes,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        if (!_registry.TryGet(step.Type, out var runner))
        {
            return new ScenarioStepResult
            {
                Name = stepName,
                Type = step.Type,
                Status = ScenarioStepRunStatus.Failed,
                StartedAt = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Message = $"Scenario step type '{step.Type}' is not registered.",
                NextEventOffset = eventOffset
            };
        }

        try
        {
            return await runner.RunAsync(
                new ScenarioStepRunContext
                {
                    ScenarioName = scenarioName,
                    StepName = stepName,
                    Step = step,
                    Events = events,
                    Lifetime = lifetime,
                    Services = services,
                    EventOffset = eventOffset,
                    ConsumedEventIndexes = consumedEventIndexes
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ScenarioStepResult
            {
                Name = stepName,
                Type = step.Type,
                Status = ScenarioStepRunStatus.Canceled,
                StartedAt = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Message = "Scenario step was canceled.",
                NextEventOffset = eventOffset
            };
        }
        catch (Exception exception)
        {
            return new ScenarioStepResult
            {
                Name = stepName,
                Type = step.Type,
                Status = ScenarioStepRunStatus.Failed,
                StartedAt = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Message = exception.Message,
                NextEventOffset = eventOffset
            };
        }
    }

    private static ScenarioRunStatus ResolveStatus(IReadOnlyList<ScenarioStepResult> results)
    {
        if (results.Any(step => step.Status == ScenarioStepRunStatus.Canceled))
        {
            return ScenarioRunStatus.Canceled;
        }

        return results.All(step => step.IsSuccess)
            ? ScenarioRunStatus.Passed
            : ScenarioRunStatus.Failed;
    }

    private static async Task<ScenarioStepResult?> DisposeLifetimeAsync(
        ScenarioRunLifetime lifetime,
        int eventOffset)
    {
        try
        {
            await lifetime.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return new ScenarioStepResult
            {
                Name = "cleanup",
                Type = "scenario.cleanup",
                Status = ScenarioStepRunStatus.Failed,
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow,
                Message = $"Scenario cleanup failed: {exception.Message}",
                NextEventOffset = eventOffset
            };
        }
    }
}
