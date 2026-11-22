using FluxFlow.Configuration.Application.Links;

namespace FluxFlow.Configuration.Components;

/// <summary>Standard outgoing links for a component. Error filters its existing Output.</summary>
public abstract record OutputComponentConfiguration
{
    public OneOrManyPortReferences Output { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Events { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Error { get; init; } = OneOrManyPortReferences.Empty;
}
