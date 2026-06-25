namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalQuery
{
    private string? _type;
    private string? _typePrefix;
    private string? _status;
    private string? _source;
    private string? _workflowId;
    private string? _workflowName;
    private string? _nodeId;
    private string? _componentId;
    private string? _subjectPrefix;
    private string? _channelPrefix;
    private string? _excludedSubjectPrefix;
    private string? _excludedChannelPrefix;
    private string? _severity;
    private string? _level;
    private IReadOnlyDictionary<string, string> _attributes = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? Type
    {
        get => _type;
        init => _type = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? TypePrefix
    {
        get => _typePrefix;
        init => _typePrefix = JournalContractNormalization.NormalizeOptional(value);
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

    public string? SubjectPrefix
    {
        get => _subjectPrefix;
        init => _subjectPrefix = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? ChannelPrefix
    {
        get => _channelPrefix;
        init => _channelPrefix = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? ExcludedSubjectPrefix
    {
        get => _excludedSubjectPrefix;
        init => _excludedSubjectPrefix = JournalContractNormalization.NormalizeOptional(value);
    }

    public string? ExcludedChannelPrefix
    {
        get => _excludedChannelPrefix;
        init => _excludedChannelPrefix = JournalContractNormalization.NormalizeOptional(value);
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

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = JournalContractNormalization.NormalizeAttributes(value, "Journal query");
    }

    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Offset { get; init; }
    public int? Limit { get; init; }
}
