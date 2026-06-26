namespace FluxFlow.Components.Journal.Contracts;

public static class JournalQueryMatcher
{
    public static void Validate(JournalQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Journal query offset cannot be negative.");
        }

        if (query.Limit is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Journal query limit must be positive.");
        }

        if (query.From.HasValue &&
            query.To.HasValue &&
            query.From.Value > query.To.Value)
        {
            throw new ArgumentException(
                "Journal query from cannot be later than to.",
                nameof(query));
        }
    }

    public static bool IsMatch(JournalRecord record, JournalQuery? query)
    {
        ArgumentNullException.ThrowIfNull(record);

        query ??= new JournalQuery();
        if (!MatchesExact(record.Type, query.Type))
        {
            return false;
        }

        if (!MatchesPrefix(record.Type, query.TypePrefix))
        {
            return false;
        }

        if (!MatchesExact(record.Status, query.Status))
        {
            return false;
        }

        if (!MatchesExact(record.Source, query.Source))
        {
            return false;
        }

        if (!MatchesExact(record.WorkflowId, query.WorkflowId))
        {
            return false;
        }

        if (!MatchesExact(record.WorkflowName, query.WorkflowName))
        {
            return false;
        }

        if (!MatchesExact(record.NodeId, query.NodeId))
        {
            return false;
        }

        if (!MatchesExact(record.ComponentId, query.ComponentId))
        {
            return false;
        }

        if (!MatchesPrefix(record.Subject, query.SubjectPrefix))
        {
            return false;
        }

        if (!MatchesPrefix(record.Channel, query.ChannelPrefix))
        {
            return false;
        }

        if (HasPrefix(record.Subject, query.ExcludedSubjectPrefix))
        {
            return false;
        }

        if (HasPrefix(record.Channel, query.ExcludedChannelPrefix))
        {
            return false;
        }

        if (!MatchesExact(record.Severity, query.Severity))
        {
            return false;
        }

        if (!MatchesExact(record.Level, query.Level))
        {
            return false;
        }

        if (query.From.HasValue && record.Timestamp < query.From.Value)
        {
            return false;
        }

        if (query.To.HasValue && record.Timestamp > query.To.Value)
        {
            return false;
        }

        if (query.Attributes is null)
        {
            return true;
        }

        foreach (var (key, expected) in query.Attributes)
        {
            if (record.Attributes is null ||
                !record.Attributes.TryGetValue(key, out var actual) ||
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
}
