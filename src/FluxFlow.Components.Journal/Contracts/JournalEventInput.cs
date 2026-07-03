namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalEventInput
{
    private string? _type;
    private string? _status;
    private string? _source;
    private string? _sourceNodeId;
    private string? _subject;
    private string? _channel;
    private string? _payloadPreview;
    private IReadOnlyDictionary<string, string> _attributes = new Dictionary<string, string>(StringComparer.Ordinal);

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

    public string? SourceNodeId
    {
        get => _sourceNodeId;
        init => _sourceNodeId = JournalContractNormalization.NormalizeOptional(value);
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

    public int? PayloadBytes { get; init; }
    public string? PayloadPreview
    {
        get => _payloadPreview;
        init => _payloadPreview = JournalContractNormalization.NormalizeOptional(value);
    }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = JournalContractNormalization.NormalizeAttributes(value, "Journal event");
    }
}
