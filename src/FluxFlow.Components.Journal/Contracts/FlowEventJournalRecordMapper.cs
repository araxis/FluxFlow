using FluxFlow.Engine.Components;

namespace FluxFlow.Components.Journal.Contracts;

public static class FlowEventJournalRecordMapper
{
    public const string WorkflowIdAttribute = "workflowId";
    public const string WorkflowNameAttribute = "workflowName";
    public const string NodeIdAttribute = "nodeId";
    public const string ComponentIdAttribute = "componentId";
    public const string SeverityAttribute = "severity";
    public const string LevelAttribute = "level";
    public const string SummaryAttribute = "summary";

    public static JournalRecord FromFlowEvent(FlowEvent flowEvent, string id)
    {
        ArgumentNullException.ThrowIfNull(flowEvent);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Journal record id is required.", nameof(id));
        }

        var attributes = CopyAttributes(flowEvent.Attributes);

        return new JournalRecord
        {
            Id = id.Trim(),
            Timestamp = flowEvent.Timestamp,
            Type = flowEvent.Type,
            Status = flowEvent.Status,
            Source = flowEvent.Source,
            WorkflowId = GetAttribute(attributes, WorkflowIdAttribute),
            WorkflowName = GetAttribute(attributes, WorkflowNameAttribute),
            NodeId = flowEvent.SourceNodeId?.ToString() ?? GetAttribute(attributes, NodeIdAttribute),
            ComponentId = GetAttribute(attributes, ComponentIdAttribute),
            Subject = flowEvent.Subject,
            Channel = flowEvent.Channel,
            Severity = GetAttribute(attributes, SeverityAttribute),
            Level = GetAttribute(attributes, LevelAttribute),
            Summary = GetAttribute(attributes, SummaryAttribute) ?? flowEvent.PayloadPreview,
            PayloadBytes = flowEvent.PayloadBytes,
            PayloadPreview = flowEvent.PayloadPreview,
            Attributes = attributes
        };
    }

    private static Dictionary<string, string> CopyAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is null)
        {
            return copy;
        }

        foreach (var (key, value) in attributes)
        {
            copy[key] = value;
        }

        return copy;
    }

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string key)
        => attributes.TryGetValue(key, out var value) ? value : null;
}
