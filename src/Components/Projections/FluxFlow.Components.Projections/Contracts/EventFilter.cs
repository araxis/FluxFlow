namespace FluxFlow.Components.Projections.Contracts;

public sealed record EventFilter
{
    private IReadOnlyDictionary<string, string> _attributes =
        ProjectionContractNormalization.CopyAttributes(null);

    public string? Type { get; init; }
    public string? TypePrefix { get; init; }
    public string? SubjectPrefix { get; init; }
    public string? ChannelPrefix { get; init; }
    public string? ExcludedSubjectPrefix { get; init; }
    public string? ExcludedChannelPrefix { get; init; }
    public string? Status { get; init; }
    public string? Source { get; init; }
    public string? SourceNodeId { get; init; }
    public string? ComponentId { get; init; }
    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = ProjectionContractNormalization.CopyAttributes(value);
    }

    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
