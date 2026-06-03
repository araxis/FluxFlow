using FluxFlow.Engine.Components;

namespace FluxFlow.Components.Projections.Contracts;

public static class EventFilterMatcher
{
    public static bool IsMatch(FlowEvent flowEvent, EventFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(flowEvent);

        filter ??= new EventFilter();
        if (!MatchesExact(flowEvent.Type, filter.Type))
        {
            return false;
        }

        if (!MatchesPrefix(flowEvent.Type, filter.TypePrefix))
        {
            return false;
        }

        if (!MatchesPrefix(flowEvent.Subject, filter.SubjectPrefix))
        {
            return false;
        }

        if (!MatchesPrefix(flowEvent.Channel, filter.ChannelPrefix))
        {
            return false;
        }

        if (HasPrefix(flowEvent.Subject, filter.ExcludedSubjectPrefix))
        {
            return false;
        }

        if (HasPrefix(flowEvent.Channel, filter.ExcludedChannelPrefix))
        {
            return false;
        }

        if (!MatchesExact(flowEvent.Status, filter.Status))
        {
            return false;
        }

        if (!MatchesExact(flowEvent.Source, filter.Source))
        {
            return false;
        }

        if (!MatchesExact(flowEvent.SourceNodeId?.ToString(), filter.SourceNodeId))
        {
            return false;
        }

        if (!MatchesExact(GetAttribute(flowEvent, "componentId"), filter.ComponentId))
        {
            return false;
        }

        if (filter.From.HasValue && flowEvent.Timestamp < filter.From.Value)
        {
            return false;
        }

        if (filter.To.HasValue && flowEvent.Timestamp > filter.To.Value)
        {
            return false;
        }

        if (filter.Attributes is null)
        {
            return true;
        }

        foreach (var (key, expected) in filter.Attributes)
        {
            if (flowEvent.Attributes is null ||
                !flowEvent.Attributes.TryGetValue(key, out var actual) ||
                !StringComparer.Ordinal.Equals(actual, expected))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesExact(string? actual, string? expected)
        => string.IsNullOrWhiteSpace(expected) ||
           StringComparer.Ordinal.Equals(actual, expected);

    private static bool MatchesPrefix(string? actual, string? expectedPrefix)
        => string.IsNullOrWhiteSpace(expectedPrefix) ||
           actual?.StartsWith(expectedPrefix, StringComparison.Ordinal) == true;

    private static bool HasPrefix(string? actual, string? expectedPrefix)
        => !string.IsNullOrWhiteSpace(expectedPrefix) &&
           actual?.StartsWith(expectedPrefix, StringComparison.Ordinal) == true;

    private static string? GetAttribute(FlowEvent flowEvent, string name)
        => flowEvent.Attributes is not null &&
           flowEvent.Attributes.TryGetValue(name, out var value)
            ? value
            : null;
}
