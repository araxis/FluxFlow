using FluxFlow.Configuration.Application.Links;

namespace FluxFlow.Configuration.Components;

/// <summary>Standard links for a component with an Input and outgoing ports.</summary>
public abstract record ComponentConfiguration : OutputComponentConfiguration
{
    public OneOrManyPortReferences Input { get; init; } = OneOrManyPortReferences.Empty;
}
