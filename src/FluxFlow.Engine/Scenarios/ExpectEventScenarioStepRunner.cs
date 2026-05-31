using FluxFlow.Engine.Components;

namespace FluxFlow.Engine.Scenarios;

public sealed class ExpectEventScenarioStepRunner : IScenarioStepRunner
{
    public const string StepType = ScenarioStepTypes.ExpectEvent;

    public string Type => StepType;

    public async Task<ScenarioStepResult> RunAsync(
        ScenarioStepRunContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var startedAt = DateTimeOffset.UtcNow;
        var expectation = ReadExpectation(context.Step.Configuration);
        var match = await context.Events.WaitForMatchAsync(
            context.EventOffset,
            expectation.Matches,
            expectation.Timeout,
            context.ConsumedEventIndexes,
            cancellationToken).ConfigureAwait(false);

        var finishedAt = DateTimeOffset.UtcNow;
        if (match is null)
        {
            var observedEvents = context.Events.SnapshotFrom(
                context.EventOffset,
                excludedIndexes: context.ConsumedEventIndexes);
            return new ScenarioStepResult
            {
                Name = context.StepName,
                Type = Type,
                Status = ScenarioStepRunStatus.TimedOut,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                Message = DescribeTimeout(expectation, observedEvents),
                NextEventOffset = context.EventOffset
            };
        }

        return new ScenarioStepResult
        {
            Name = context.StepName,
            Type = Type,
            Status = ScenarioStepRunStatus.Passed,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Message = "Expected event observed.",
            MatchedEvent = match.Event,
            MatchedEventIndex = match.Index,
            NextEventOffset = context.EventOffset
        };
    }

    private static FlowEventExpectation ReadExpectation(
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> configuration)
        => new()
        {
            EventType = ScenarioStepConfigurationReader.ReadString(configuration, ScenarioStepConfigurationKeys.EventType),
            TopicStartsWith = ScenarioStepConfigurationReader.ReadString(configuration, ScenarioStepConfigurationKeys.TopicStartsWith),
            SubjectStartsWith = ScenarioStepConfigurationReader.ReadString(configuration, ScenarioStepConfigurationKeys.SubjectStartsWith),
            Status = ScenarioStepConfigurationReader.ReadString(configuration, ScenarioStepConfigurationKeys.Status),
            Source = ScenarioStepConfigurationReader.ReadString(configuration, ScenarioStepConfigurationKeys.Source),
            PayloadContains = ScenarioStepConfigurationReader.ReadString(configuration, ScenarioStepConfigurationKeys.PayloadContains),
            Attributes = ScenarioStepConfigurationReader.ReadStringMap(configuration, ScenarioStepConfigurationKeys.Attributes),
            Timeout = TimeSpan.FromMilliseconds(ScenarioStepConfigurationReader.ReadIntOrDefault(
                configuration,
                ScenarioStepConfigurationKeys.TimeoutMs,
                5000,
                1))
        };

    private static string DescribeTimeout(
        FlowEventExpectation expectation,
        IReadOnlyList<FlowEvent> observedEvents)
    {
        var parts = new List<string>();
        Add(parts, "type", expectation.EventType);
        Add(parts, "topic", expectation.TopicStartsWith);
        Add(parts, "subject", expectation.SubjectStartsWith);
        Add(parts, "status", expectation.Status);
        Add(parts, "source", expectation.Source);
        Add(parts, "payload", expectation.PayloadContains);

        foreach (var attribute in expectation.Attributes)
        {
            Add(parts, $"attribute {attribute.Key}", attribute.Value);
        }

        var detail = parts.Count == 0
            ? "any event"
            : string.Join(", ", parts);

        var observed = observedEvents.Count == 0
            ? "Observed 0 app runtime events while waiting."
            : $"Observed while waiting: {string.Join("; ", observedEvents.Select(DescribeObservedEvent))}.";
        var guidance =
            "Finished runs do not keep listening; rerun the test and produce a matching app runtime event before the timeout.";

        return $"Expected event was not observed within {expectation.Timeout.TotalMilliseconds:0} ms ({detail}). {observed} {guidance}";
    }

    private static void Add(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label} '{value}'");
        }
    }

    private static string DescribeObservedEvent(FlowEvent flowEvent)
    {
        var parts = new List<string> { flowEvent.Type };
        Add(parts, "topic", flowEvent.Topic);
        Add(parts, "status", flowEvent.Status);
        Add(parts, "source", flowEvent.Source);

        if (!string.IsNullOrWhiteSpace(flowEvent.PayloadPreview))
        {
            Add(parts, "payload", Shorten(flowEvent.PayloadPreview));
        }

        return string.Join(", ", parts);
    }

    private static string Shorten(string value)
        => value.Length <= 80 ? value : $"{value[..77]}...";
}
