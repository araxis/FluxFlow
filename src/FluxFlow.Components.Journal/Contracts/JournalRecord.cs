namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalRecord
{
    private string _id = string.Empty;
    private string? _type;
    private string? _status;
    private string? _source;
    private string? _workflowId;
    private string? _workflowName;
    private string? _nodeId;
    private string? _componentId;
    private string? _subject;
    private string? _channel;
    private string? _severity;
    private string? _level;
    private string? _summary;
    private string? _payloadPreview;
    private IReadOnlyDictionary<string, string> _attributes = new Dictionary<string, string>(StringComparer.Ordinal);

    public required string Id
    {
        get => _id;
        init => _id = JournalContractNormalization.NormalizeRequired(value);
    }

    public required DateTimeOffset Timestamp { get; init; }
    public string? Type
    {
        get => _type;
        init => _type = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? Status
    {
        get => _status;
        init => _status = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? Source
    {
        get => _source;
        init => _source = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? WorkflowId
    {
        get => _workflowId;
        init => _workflowId = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? WorkflowName
    {
        get => _workflowName;
        init => _workflowName = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? NodeId
    {
        get => _nodeId;
        init => _nodeId = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? ComponentId
    {
        get => _componentId;
        init => _componentId = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? Subject
    {
        get => _subject;
        init => _subject = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? Channel
    {
        get => _channel;
        init => _channel = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? Severity
    {
        get => _severity;
        init => _severity = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? Level
    {
        get => _level;
        init => _level = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? Summary
    {
        get => _summary;
        init => _summary = JournalContractNormalization.NormalizeOptional(value);
    }

    public int? PayloadBytes { get; init; }
    public string? PayloadPreview
    {
        get => _payloadPreview;
        init => _payloadPreview = JournalContractNormalization.NormalizeOptional(value);
    }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = JournalContractNormalization.NormalizeAttributes(value, "Journal record");
    }
}
