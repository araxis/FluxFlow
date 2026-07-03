using FluxFlow.Components.Journal.Contracts;

namespace FluxFlow.Components.Journal;

public sealed class JournalEventInputBuilder
{
    private readonly Dictionary<string, string> attributes = new(StringComparer.Ordinal);
    private DateTimeOffset? timestamp;
    private string? type;
    private string? status;
    private string? source;
    private string? sourceNodeId;
    private string? subject;
    private string? channel;
    private int? payloadBytes;
    private string? payloadPreview;

    public static JournalEventInputBuilder Create(DateTimeOffset timestamp)
        => new JournalEventInputBuilder().WithTimestamp(timestamp);

    public JournalEventInputBuilder WithTimestamp(DateTimeOffset value)
    {
        if (value == default)
        {
            throw new ArgumentException("Journal event timestamp is required.", nameof(value));
        }

        timestamp = value;
        return this;
    }

    public JournalEventInputBuilder WithType(string? value)
    {
        type = value;
        return this;
    }

    public JournalEventInputBuilder WithStatus(string? value)
    {
        status = value;
        return this;
    }

    public JournalEventInputBuilder WithSource(string? value)
    {
        source = value;
        return this;
    }

    public JournalEventInputBuilder WithSourceNodeId(string? value)
    {
        sourceNodeId = value;
        return this;
    }

    public JournalEventInputBuilder WithSubject(string? value)
    {
        subject = value;
        return this;
    }

    public JournalEventInputBuilder WithChannel(string? value)
    {
        channel = value;
        return this;
    }

    public JournalEventInputBuilder WithPayload(int? bytes = null, string? preview = null)
    {
        payloadBytes = bytes;
        payloadPreview = preview;
        return this;
    }

    public JournalEventInputBuilder WithPayloadBytes(int? value)
    {
        payloadBytes = value;
        return this;
    }

    public JournalEventInputBuilder WithPayloadPreview(string? value)
    {
        payloadPreview = value;
        return this;
    }

    public JournalEventInputBuilder WithWorkflow(string workflowId, string? workflowName = null)
    {
        AddAttribute(JournalRecordMapper.WorkflowIdAttribute, workflowId);
        if (workflowName is not null)
        {
            AddAttribute(JournalRecordMapper.WorkflowNameAttribute, workflowName);
        }

        return this;
    }

    public JournalEventInputBuilder WithNode(string nodeId)
        => AddAttribute(JournalRecordMapper.NodeIdAttribute, nodeId);

    public JournalEventInputBuilder WithComponent(string componentId)
        => AddAttribute(JournalRecordMapper.ComponentIdAttribute, componentId);

    public JournalEventInputBuilder WithSeverity(string severity)
        => AddAttribute(JournalRecordMapper.SeverityAttribute, severity);

    public JournalEventInputBuilder WithLevel(string level)
        => AddAttribute(JournalRecordMapper.LevelAttribute, level);

    public JournalEventInputBuilder WithSummary(string summary)
        => AddAttribute(JournalRecordMapper.SummaryAttribute, summary);

    public JournalEventInputBuilder AddAttribute(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        attributes.Add(key, value);
        return this;
    }

    public JournalEventInputBuilder AddAttributes(
        IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var (key, value) in values)
        {
            AddAttribute(key, value);
        }

        return this;
    }

    public JournalEventInput BuildInput()
    {
        if (!timestamp.HasValue)
        {
            throw new InvalidOperationException("Journal event timestamp is required.");
        }

        return new JournalEventInput
        {
            Timestamp = timestamp.Value,
            Type = type,
            Status = status,
            Source = source,
            SourceNodeId = sourceNodeId,
            Subject = subject,
            Channel = channel,
            PayloadBytes = payloadBytes,
            PayloadPreview = payloadPreview,
            Attributes = attributes
        };
    }

    public JournalRecord BuildRecord(string id)
        => JournalRecordMapper.FromEvent(BuildInput(), id);
}
