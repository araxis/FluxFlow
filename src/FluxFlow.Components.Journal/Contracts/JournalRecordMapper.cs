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
            Type = input.Type,
            Status = input.Status,
            Source = input.Source,
            WorkflowId = GetAttribute(attributes, WorkflowIdAttribute),
            WorkflowName = GetAttribute(attributes, WorkflowNameAttribute),
            NodeId = input.SourceNodeId ?? GetAttribute(attributes, NodeIdAttribute),
            ComponentId = GetAttribute(attributes, ComponentIdAttribute),
            Subject = input.Subject,
            Channel = input.Channel,
            Severity = GetAttribute(attributes, SeverityAttribute),
            Level = GetAttribute(attributes, LevelAttribute),
            Summary = GetAttribute(attributes, SummaryAttribute) ?? input.PayloadPreview,
            PayloadBytes = input.PayloadBytes,
            PayloadPreview = input.PayloadPreview,
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
            copy[key] = value;
        }

        return copy;
    }

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string key)
        => attributes.TryGetValue(key, out var value) ? value : null;
}
