using FluxFlow.Configuration.Application.Links;

namespace FluxFlow.Configuration.Components.Routing;

/// <summary>Serializable Gate settings; port names and wiring belong to registration.</summary>
public sealed record GateConfiguration : ComponentConfiguration
{
    public OneOrManyPortReferences Open { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Close { get; init; } = OneOrManyPortReferences.Empty;

    public bool InitiallyOpen { get; init; }
    public int OutputCapacity { get; init; } = 128;

    public void Validate()
    {
        if (OutputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(OutputCapacity));
    }
}
