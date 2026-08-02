using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionContentRecordInput
{
    private string? _type;
    private string? _name;
    private FlowContent? _content;
    private IReadOnlyDictionary<string, string> _attributes =
        SessionContentContractMap.CopyAttributes(null);

    public DateTimeOffset? Timestamp { get; init; }

    public string? Type
    {
        get => _type;
        init => _type = SessionContentContractMap.NormalizeOptional(value);
    }

    public string? Name
    {
        get => _name;
        init => _name = SessionContentContractMap.NormalizeOptional(value);
    }

    public required FlowContent Content
    {
        get => _content!;
        init => _content = value;
    }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = SessionContentContractMap.CopyAttributes(value);
    }
}
