namespace FluxFlow.ComponentPackageTemplate.Contracts;

public sealed record TemplateInput
{
    public required string Id { get; init; }
    public required string Value { get; init; }
}
