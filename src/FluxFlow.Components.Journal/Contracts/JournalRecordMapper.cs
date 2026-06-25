namespace FluxFlow.Components.Journal.Contracts;

public static class JournalRecordMapper
{
    public const string WorkflowIdAttribute = "workflowId";
    public const string WorkflowNameAttribute = "workflowName";
    public const string NodeIdAttribute = "nodeId";
    public const string ComponentIdAttribute = "componentId";
    public const string SeverityAttribute = "severity";
    public const string LevelAttribute = "level";
    public const string SummaryAttribute = "summary";

    public static JournalRecord FromEvent(JournalEventInput input, string id)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Journal record id is required.", nameof(id));

        var attributes = CopyAttributes(input.Attributes);

        return new JournalRecord
        {
            Id = id.Trim(),
            Timestamp = input.Timestamp,
            Type = NormalizeOptional(input.Type),
            Status = NormalizeOptional(input.Status),
            Source = NormalizeOptional(input.Source),
            WorkflowId = GetAttribute(attributes, WorkflowIdAttribute),
            WorkflowName = GetAttribute(attributes, WorkflowNameAttribute),
            NodeId = NormalizeOptional(input.SourceNodeId) ?? GetAttribute(attributes, NodeIdAttribute),
            ComponentId = GetAttribute(attributes, ComponentIdAttribute),
            Subject = NormalizeOptional(input.Subject),
            Channel = NormalizeOptional(input.Channel),
            Severity = GetAttribute(attributes, SeverityAttribute),
            Level = GetAttribute(attributes, LevelAttribute),
            Summary = GetAttribute(attributes, SummaryAttribute) ?? NormalizeOptional(input.PayloadPreview),
            PayloadBytes = input.PayloadBytes,
            PayloadPreview = NormalizeOptional(input.PayloadPreview),
            Attributes = attributes
        };
    }

    private static Dictionary<string, string> CopyAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is null)
            return copy;

        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Journal event attribute keys are required.");

            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Journal event attribute values are required.");

            var normalizedKey = key.Trim();
            if (!copy.TryAdd(normalizedKey, value.Trim()))
            {
                throw new ArgumentException(
                    $"Journal event attribute '{normalizedKey}' is declared more than once.");
            }
        }

        return copy;
    }

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string key)
        => attributes.TryGetValue(key, out var value) ? NormalizeOptional(value) : null;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
