using FluxFlow.Components.Designer.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class DesignMetadataFactoryTests
{
    [Fact]
    public void OptionFactoriesCreateExpectedShapes()
    {
        var options = new[]
        {
            OptionDesignMetadataFactory.Text(
                "name", "Name", "Name help.", "General", "advanced", "sample"),
            OptionDesignMetadataFactory.Number(
                "count", "Count", "Count help.", "Limits", "primary", 5, 1, 10),
            OptionDesignMetadataFactory.Boolean(
                "enabled", "Enabled", "Enabled help.", "General", "advanced", true),
            OptionDesignMetadataFactory.Json(
                "shape", "Shape", "Shape help.", "Data", "primary", relatedResource: "selector"),
            OptionDesignMetadataFactory.Expression(
                "predicate", "Predicate", "Predicate help.", "Filtering", "primary", relatedResource: "engine")
        };

        options.Select(option => option.Kind).ShouldBe([
            OptionValueKind.Text,
            OptionValueKind.Number,
            OptionValueKind.Boolean,
            OptionValueKind.Json,
            OptionValueKind.Expression
        ]);
        options[0].DefaultValue.ShouldBe("sample");
        options[1].Min.ShouldBe(1);
        options[1].Max.ShouldBe(10);
        options[2].Attributes.ContainsKey(
            new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor)).ShouldBeFalse();
        options[3].Attributes[new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.RelatedResource)].Value.ShouldBe("selector");
        options[4].Attributes[new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.Syntax)].Value.ShouldBe("expression");
    }

    [Fact]
    public void StandardOptionFactoriesPreserveRuntimeAndTypeHints()
    {
        var capacity = OptionDesignMetadataFactory.BoundedCapacity(128);
        var typeName = OptionDesignMetadataFactory.TypeName(
            "inputType", "Input Type", "System.Object", "Input type help.");

        capacity.Name.Value.ShouldBe("boundedCapacity");
        capacity.DefaultValue.ShouldBe(128);
        capacity.Min.ShouldBe(1);
        capacity.Attributes[new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.Section)].Value.ShouldBe("Runtime");
        typeName.Attributes[new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.Section)].Value.ShouldBe("Type Metadata");
    }

    [Fact]
    public void ResourceFactoriesCreateHostOwnedPickerHints()
    {
        var selector = ResourceDesignMetadataFactory.HostOwned(
            "selector",
            ResourceDesignMetadataAttributeValues.Delegate,
            "Selector",
            0,
            "Required selector.",
            "Func<object,string>",
            isRequired: true,
            keyPattern: "delegate:{name}",
            option: "selectorName");
        var clock = ResourceDesignMetadataFactory.Clock(
            "clock", 1, "Optional deterministic clock.");

        selector.IsRequired.ShouldBeTrue();
        selector.Attributes[new ComponentAttributeName(
            ResourceDesignMetadataAttributeNames.PickerKind)].Value.ShouldBe("delegate");
        selector.Attributes[new ComponentAttributeName(
            ResourceDesignMetadataAttributeNames.Option)].Value.ShouldBe("selectorName");
        clock.ValueType?.Value.ShouldBe(nameof(TimeProvider));
        clock.Attributes[new ComponentAttributeName(
            ResourceDesignMetadataAttributeNames.KeyPattern)].Value.ShouldBe("clock:{name}");
    }
}
