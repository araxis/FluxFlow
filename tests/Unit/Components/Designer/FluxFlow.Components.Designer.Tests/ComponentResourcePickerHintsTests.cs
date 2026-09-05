using FluxFlow.Components.Designer.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentResourcePickerHintsTests
{
    [Fact]
    public void Create_returns_host_owned_resource_picker_hints_from_metadata()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.resource-picker"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("clockResource"),
                    Kind = OptionValueKind.Text
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("usesClock"),
                    Kind = OptionValueKind.Boolean
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("expression"),
                    Kind = OptionValueKind.Expression
                }
            ],
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("clock"),
                    DisplayName = new ComponentMetadataText("Clock"),
                    Order = 0,
                    Summary = new ComponentMetadataText("Optional clock resource."),
                    ValueType = new ComponentValueTypeHint(nameof(TimeProvider)),
                    IsRequired = true,
                    Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                        ResourceDesignMetadataAttributeValues.Clock,
                        keyPattern: "clock:{name}",
                        option: "clockResource",
                        requiredWhenAnyOption: "usesClock, expression")
                }
            ]
        };

        var hint = ComponentResourcePickerHints.Create(metadata).ShouldHaveSingleItem();

        hint.ComponentType.ShouldBe(new ComponentType("sample.resource-picker"));
        hint.ResourceName.ShouldBe(new ComponentResourceName("clock"));
        hint.PickerKind.ShouldBe(ResourceDesignMetadataAttributeValues.Clock);
        hint.KeyPattern.ShouldBe("clock:{name}");
        hint.RelatedOption.ShouldBe(new ComponentOptionName("clockResource"));
        hint.RequiredWhenAnyOptions.ShouldBe([
            new ComponentOptionName("usesClock"),
            new ComponentOptionName("expression")
        ]);
        hint.IsRequired.ShouldBeTrue();
        hint.ValueType.ShouldBe(new ComponentValueTypeHint(nameof(TimeProvider)));
        hint.DisplayName.ShouldBe(new ComponentMetadataText("Clock"));
        hint.Summary.ShouldBe(new ComponentMetadataText("Optional clock resource."));
    }

    [Fact]
    public void Create_filters_resources_without_host_owned_picker_attributes()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.filters"),
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("plain"),
                    Order = 0
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("hostOwnedWithoutPicker"),
                    Order = 1,
                    Attributes = AttributeMap(
                        (ResourceDesignMetadataAttributeNames.Ownership,
                            ResourceDesignMetadataAttributeValues.HostOwned))
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("packageOwned"),
                    Order = 2,
                    Attributes = AttributeMap(
                        (ResourceDesignMetadataAttributeNames.Ownership, "package-owned"),
                        (ResourceDesignMetadataAttributeNames.PickerKind,
                            ResourceDesignMetadataAttributeValues.Clock))
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("clock"),
                    Order = 3,
                    Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                        ResourceDesignMetadataAttributeValues.Clock)
                }
            ]
        };

        var hint = ComponentResourcePickerHints.Create(metadata).ShouldHaveSingleItem();

        hint.ResourceName.ShouldBe(new ComponentResourceName("clock"));
        hint.PickerKind.ShouldBe(ResourceDesignMetadataAttributeValues.Clock);
    }

    [Fact]
    public void Create_preserves_resource_order_within_metadata()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.order"),
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("store"),
                    Order = 1,
                    Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                        ResourceDesignMetadataAttributeValues.Store)
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("clock"),
                    Order = 0,
                    Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                        ResourceDesignMetadataAttributeValues.Clock)
                }
            ]
        };

        var hints = ComponentResourcePickerHints.Create(metadata);

        hints.Select(hint => hint.ResourceName.Value).ShouldBe(["clock", "store"]);
    }

    [Fact]
    public void Create_from_catalog_returns_deterministic_component_order()
    {
        var catalog = new ComponentDesignMetadataCatalog(
        [
            CreateMetadata("sample.two", "clock", ResourceDesignMetadataAttributeValues.Clock),
            CreateMetadata("sample.one", "store", ResourceDesignMetadataAttributeValues.Store)
        ]);

        var hints = ComponentResourcePickerHints.Create(catalog);

        hints.Select(hint => hint.ComponentType.Value).ShouldBe([
            "sample.one",
            "sample.one",
            "sample.two",
            "sample.two"
        ]);
        hints.Select(hint => hint.ResourceName.Value).ShouldBe([
            "store",
            "processing",
            "clock",
            "processing"
        ]);
    }

    private static ComponentDesignMetadata CreateMetadata(
        string componentType,
        string resourceName,
        string pickerKind)
        => new()
        {
            Type = new ComponentType(componentType),
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName(resourceName),
                    Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(pickerKind)
                }
            ]
        };

    private static Dictionary<ComponentAttributeName, ComponentAttributeValue> AttributeMap(
        params (string Name, string Value)[] attributes)
        => attributes.ToDictionary(
            attribute => new ComponentAttributeName(attribute.Name),
            attribute => new ComponentAttributeValue(attribute.Value));
}
