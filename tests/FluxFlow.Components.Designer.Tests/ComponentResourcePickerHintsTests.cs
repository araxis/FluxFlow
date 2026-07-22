using FluxFlow.Components.Designer.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentResourcePickerHintsTests
{
    [Fact]
    public void Create_returns_host_owned_resource_picker_hints_from_metadata()
    {
        var metadata = new ComponentDesignMetadataBuilder("sample.resource-picker")
            .AddOption("clockResource", OptionValueKind.Text)
            .AddOption("usesClock", OptionValueKind.Boolean)
            .AddOption("expression", OptionValueKind.Expression)
            .AddResource(
                "clock",
                displayName: "Clock",
                order: 0,
                summary: "Optional clock resource.",
                valueType: nameof(TimeProvider),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}",
                    option: "clockResource",
                    requiredWhenAnyOption: "usesClock, expression"))
            .Build();

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
        var metadata = new ComponentDesignMetadataBuilder("sample.order")
            .AddResource(
                "store",
                order: 1,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Store))
            .AddResource(
                "clock",
                order: 0,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock))
            .Build();

        var hints = ComponentResourcePickerHints.Create(metadata);

        hints.Select(hint => hint.ResourceName.Value).ShouldBe(["clock", "store"]);
    }

    [Fact]
    public void Create_from_catalog_returns_deterministic_component_order()
    {
        var catalog = new ComponentDesignMetadataCatalog()
            .Add(CreateMetadata("sample.two", "clock", ResourceDesignMetadataAttributeValues.Clock))
            .Add(CreateMetadata("sample.one", "store", ResourceDesignMetadataAttributeValues.Store));

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
        => new ComponentDesignMetadataBuilder(componentType)
            .AddResource(
                resourceName,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(pickerKind))
            .Build();

    private static Dictionary<ComponentAttributeName, ComponentAttributeValue> AttributeMap(
        params (string Name, string Value)[] attributes)
        => attributes.ToDictionary(
            attribute => new ComponentAttributeName(attribute.Name),
            attribute => new ComponentAttributeValue(attribute.Value));
}
