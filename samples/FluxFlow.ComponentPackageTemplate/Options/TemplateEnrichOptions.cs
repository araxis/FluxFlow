namespace FluxFlow.ComponentPackageTemplate.Options;

public sealed record TemplateEnrichOptions
{
    public string Prefix { get; init; } = "processed";
    public int BoundedCapacity { get; init; } = 128;
}
