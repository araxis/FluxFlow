using System.Collections.ObjectModel;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

internal static class ComponentDesignMetadataSnapshot
{
    internal static ComponentDesignMetadata Create(ComponentDesignMetadata metadata)
        => metadata with
        {
            Options = Array.AsReadOnly(metadata.Options.Select(Create).ToArray()),
            Resources = Array.AsReadOnly(metadata.Resources.Select(Create).ToArray()),
            Ports = Array.AsReadOnly(metadata.Ports.Select(Create).ToArray()),
            Attributes = Copy(metadata.Attributes)
        };

    private static OptionDesignMetadata Create(OptionDesignMetadata option)
        => option with
        {
            Choices = Array.AsReadOnly(option.Choices.Select(Create).ToArray()),
            Attributes = Copy(option.Attributes)
        };

    private static OptionChoiceMetadata Create(OptionChoiceMetadata choice)
        => choice with
        {
            Attributes = Copy(choice.Attributes)
        };

    private static ResourceDesignMetadata Create(ResourceDesignMetadata resource)
        => resource with
        {
            Attributes = Copy(resource.Attributes)
        };

    private static PortDesignMetadata Create(PortDesignMetadata port)
        => port with
        {
            Attributes = Copy(port.Attributes)
        };

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Copy(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes)
        => new ReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue>(
            new Dictionary<ComponentAttributeName, ComponentAttributeValue>(attributes));
}
