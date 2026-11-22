using FluxFlow.Configuration.Sources;

namespace FluxFlow.Configuration.Components.Sources;

/// <summary>A JSON source preserves heterogeneous sample types through IConfiguration.</summary>
public sealed record SourceConfiguration : ComponentConfiguration
{
    public JsonSource? Values { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
