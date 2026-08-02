using FluxFlow.Composition.Addressing;

namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerResourceReference
{
    public required ApplicationAddress Component { get; init; }

    public required string PropertyName { get; init; }

    public required string Reference { get; init; }

    public ApplicationAddress? Address { get; init; }

    public bool IsRequired { get; init; }

    public bool Exists { get; init; }
}
