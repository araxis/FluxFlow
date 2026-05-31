using FluxFlow.Engine.Components;

namespace FluxFlow.Engine.Scenarios;

public sealed record FlowEventExpectation
{
    public string? EventType { get; init; }
    public string? TopicStartsWith { get; init; }
    public string? SubjectStartsWith { get; init; }
    public string? Status { get; init; }
    public string? Source { get; init; }
    public string? PayloadContains { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public bool Matches(FlowEvent flowEvent)
    {
        ArgumentNullException.ThrowIfNull(flowEvent);

        return MatchesExact(flowEvent.Type, EventType) &&
               MatchesPrefix(flowEvent.Topic, TopicStartsWith) &&
               MatchesPrefix(flowEvent.Subject, SubjectStartsWith) &&
               MatchesExact(flowEvent.Status, Status) &&
               MatchesExact(flowEvent.Source, Source) &&
               MatchesContains(flowEvent.PayloadPreview, PayloadContains) &&
               Attributes.All(attribute => MatchesAttribute(flowEvent.GetAttribute(attribute.Key), attribute.Value));
    }

    private static bool MatchesExact(string? actual, string? expected)
        => string.IsNullOrWhiteSpace(expected) ||
           string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool MatchesPrefix(string? actual, string? expectedPrefix)
        => string.IsNullOrWhiteSpace(expectedPrefix) ||
           (!string.IsNullOrWhiteSpace(actual) &&
            actual.StartsWith(expectedPrefix, StringComparison.Ordinal));

    private static bool MatchesContains(string? actual, string? expectedValue)
        => string.IsNullOrWhiteSpace(expectedValue) ||
           (!string.IsNullOrEmpty(actual) &&
            actual.Contains(expectedValue, StringComparison.Ordinal));

    private static bool MatchesAttribute(string? actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        if (bool.TryParse(actual, out var actualBoolean) &&
            bool.TryParse(expected, out var expectedBoolean))
        {
            return actualBoolean == expectedBoolean;
        }

        return string.Equals(actual, expected, StringComparison.Ordinal);
    }
}
