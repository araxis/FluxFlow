using FluxFlow.Configuration.Links;

namespace FluxFlow.Configuration.Sources;

/// <summary>JSON text preserves heterogeneous sample types through IConfiguration's string values.</summary>
public sealed record SourceConfiguration
{
    public OneOrManyLinks Input { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Output { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Events { get; init; } = OneOrManyLinks.Empty;

    public string? ValuesJson { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
