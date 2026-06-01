namespace FluxFlow.ComponentPackageTemplate.Contracts;

public sealed record TemplateOutput
{
    public required string Id { get; init; }
    public required string Value { get; init; }
    public required string Text { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
}
