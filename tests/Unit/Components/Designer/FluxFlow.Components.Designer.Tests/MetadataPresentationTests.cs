using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class MetadataPresentationTests
{
    [Fact]
    public void Presentation_edits_preserve_structure_and_leave_original_unchanged()
    {
        var original = CreateMetadata();
        var edited = original
            .WithDisplay("Presented", "Tools", "Summary", "icon", "sample", 480)
            .WithOption(" mode ", OptionValueKind.Enum, "Mode", "Select mode",
                defaultValue: "fast", min: 1, max: 9, section: "Options",
                importance: "primary", editor: "select", syntax: "literal", relatedResource: "store")
            .WithResource(" store ", "Storage", 7, "Borrowed store", "Store interface",
                ownership: "host-owned", pickerKind: "store", keyPattern: "stores:{name}",
                option: "mode", requiredWhenAnyOption: "enabled")
            .WithPort(" shared ", PortDirection.Input, "Receive", "Messages", 8, "Request", true)
            .WithPort("cancel", PortDirection.Input, summary: "Cancel request");

        edited.Type.ShouldBe(original.Type);
        edited.ProcessingCapabilities.ShouldBe(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        edited.DisplayName?.Value.ShouldBe("Presented");
        edited.Category?.Value.ShouldBe("Tools");
        edited.Summary?.Value.ShouldBe("Summary");
        edited.IconKey?.Value.ShouldBe("icon");
        edited.PreferredNodeName?.Value.ShouldBe("sample");
        edited.SuggestedEditorWidth.ShouldBe(480);

        var option = edited.Options[0];
        option.Name.ShouldBe(original.Options[0].Name);
        option.IsRequired.ShouldBeTrue();
        option.Kind.ShouldBe(OptionValueKind.Enum);
        option.DisplayName?.Value.ShouldBe("Mode");
        option.HelperText?.Value.ShouldBe("Select mode");
        option.DefaultValue.ShouldBe("fast");
        option.Min.ShouldBe(1);
        option.Max.ShouldBe(9);
        option.Attributes[new("custom")].Value.ShouldBe("retained");
        option.Attributes[new(OptionDesignMetadataAttributeNames.Section)].Value.ShouldBe("Options");
        option.Attributes[new(OptionDesignMetadataAttributeNames.Importance)].Value.ShouldBe("primary");
        option.Attributes[new(OptionDesignMetadataAttributeNames.Editor)].Value.ShouldBe("select");
        option.Attributes[new(OptionDesignMetadataAttributeNames.Syntax)].Value.ShouldBe("literal");
        option.Attributes[new(OptionDesignMetadataAttributeNames.RelatedResource)].Value.ShouldBe("store");
        option.Choices.ShouldBeSameAs(original.Options[0].Choices);
        edited.Options[1].ShouldBeSameAs(original.Options[1]);

        var resource = edited.Resources[0];
        resource.IsRequired.ShouldBeTrue();
        resource.Name.ShouldBe(original.Resources[0].Name);
        resource.DisplayName?.Value.ShouldBe("Storage");
        resource.Order.ShouldBe(7);
        resource.Summary?.Value.ShouldBe("Borrowed store");
        resource.ValueType?.Value.ShouldBe("Store interface");
        resource.Attributes[new("custom")].Value.ShouldBe("retained");
        resource.Attributes[new(ResourceDesignMetadataAttributeNames.Ownership)].Value.ShouldBe("host-owned");
        resource.Attributes[new(ResourceDesignMetadataAttributeNames.PickerKind)].Value.ShouldBe("store");
        resource.Attributes[new(ResourceDesignMetadataAttributeNames.KeyPattern)].Value.ShouldBe("stores:{name}");
        resource.Attributes[new(ResourceDesignMetadataAttributeNames.Option)].Value.ShouldBe("mode");
        resource.Attributes[new(ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption)].Value.ShouldBe("enabled");
        edited.Resources[1].ShouldBeSameAs(original.Resources[1]);

        var port = edited.Ports[0];
        port.Name.ShouldBe(original.Ports[0].Name);
        port.Direction.ShouldBe(PortDirection.Input);
        port.DisplayName?.Value.ShouldBe("Receive");
        port.Group?.Value.ShouldBe("Messages");
        port.Order.ShouldBe(8);
        port.Summary?.Value.ShouldBe("Request");
        port.IsPrimary.ShouldBeTrue();
        port.MessageType.ShouldBe(original.Ports[0].MessageType);
        port.ValueType.ShouldBe(original.Ports[0].ValueType);
        port.Kind.ShouldBe(original.Ports[0].Kind);
        port.LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        port.InputShape.ShouldBeSameAs(original.Ports[0].InputShape);
        port.Attributes.ShouldBeSameAs(original.Ports[0].Attributes);
        port.EventShapes.ShouldBeSameAs(original.Ports[0].EventShapes);
        edited.Ports[1].ShouldBeSameAs(original.Ports[1]);
        edited.Ports[2].Kind.ShouldBe(ComponentPortKind.Signal);
        edited.Ports[2].Summary?.Value.ShouldBe("Cancel request");
        edited.Ports[2].DisplayName.ShouldBe(original.Ports[2].DisplayName);

        original.DisplayName?.Value.ShouldBe("Original");
        original.Options[0].Kind.ShouldBe(OptionValueKind.Text);
        original.Options[0].DefaultValue.ShouldBeNull();
        original.Options[0].Attributes.Count.ShouldBe(1);
        original.Resources[0].Order.ShouldBe(2);
        original.Resources[0].ValueType?.Value.ShouldBe("OriginalStore");
        original.Ports[0].Order.ShouldBe(3);
        original.Ports[0].Summary.ShouldBeNull();
        original.Ports[2].Summary.ShouldBeNull();
    }

    [Fact]
    public void Partial_presentation_edits_preserve_hints_and_select_exact_port_direction()
    {
        var original = CreateMetadata()
            .WithOption("mode", OptionValueKind.Text, section: "Existing")
            .WithResource("store", "Store", ownership: "borrowed")
            .WithPort("shared", PortDirection.Output, "Publish", "Data", 9, "Result", true);

        var edited = original
            .WithOption("mode", OptionValueKind.Enum)
            .WithResource("store", null)
            .WithPort("shared", PortDirection.Output)
            .WithAttribute("new", "value", appendExistingWith: ",");

        edited.Options[0].Attributes[new(OptionDesignMetadataAttributeNames.Section)].Value.ShouldBe("Existing");
        edited.Resources[0].Attributes[new(ResourceDesignMetadataAttributeNames.Ownership)].Value.ShouldBe("borrowed");
        edited.Resources[0].ValueType?.Value.ShouldBe("OriginalStore");
        edited.Ports[0].ShouldBeSameAs(original.Ports[0]);
        edited.Ports[1].ShouldBe(original.Ports[1]);
        edited.Ports[1].DisplayName?.Value.ShouldBe("Publish");
        edited.Ports[1].Group?.Value.ShouldBe("Data");
        edited.Ports[1].Order.ShouldBe(9);
        edited.Ports[1].Summary?.Value.ShouldBe("Result");
        edited.Ports[1].IsPrimary.ShouldBeTrue();
        edited.Attributes[new("new")].Value.ShouldBe("value");
        original.Attributes.ContainsKey(new("new")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("option", null)]
    [InlineData("option", "")]
    [InlineData("option", "  ")]
    [InlineData("choice-option", null)]
    [InlineData("choice-option", "")]
    [InlineData("choice-option", "  ")]
    [InlineData("choice-value", null)]
    [InlineData("choice-value", "")]
    [InlineData("choice-value", "  ")]
    [InlineData("resource", null)]
    [InlineData("resource", "")]
    [InlineData("resource", "  ")]
    [InlineData("port", null)]
    [InlineData("port", "")]
    [InlineData("port", "  ")]
    public void Presentation_targets_and_choices_require_nonempty_names(string kind, string? value)
    {
        var original = CreateMetadata();
        var edits = new Dictionary<string, (Action Edit, string Parameter)>
        {
            ["option"] = (() => original.WithOption(value!, OptionValueKind.Text), "name"),
            ["choice-option"] = (() => original.WithOptionChoice(value!, "fast"), "optionName"),
            ["choice-value"] = (() => original.WithOptionChoice("mode", value!), "value"),
            ["resource"] = (() => original.WithResource(value!, "Store"), "name"),
            ["port"] = (() => original.WithPort(value!, PortDirection.Input), "name")
        };
        var (edit, parameter) = edits[kind];
        Assert.ThrowsAny<ArgumentException>(edit).ParamName.ShouldBe(parameter);
        original.Options[0].Choices.ShouldHaveSingleItem().Value.Value.ShouldBe("slow");
    }

    [Fact]
    public void Choice_and_attribute_edits_compose_without_mutating_prior_metadata()
    {
        var original = CreateMetadata();
        var edited = original.WithOptionChoice(" mode ", " fast ", "Fast", "Low latency")
            .WithAttribute("omittedOptions", "timeZone", appendExistingWith: ",")
            .WithAttribute("custom", "replaced");
        edited.Options[0].Choices.Select(choice => choice.Value.Value).ShouldBe(["slow", "fast"]);
        edited.Options[0].Choices[1].DisplayName?.Value.ShouldBe("Fast");
        edited.Options[0].Choices[1].HelperText?.Value.ShouldBe("Low latency");
        edited.Options[0].IsRequired.ShouldBeTrue();
        edited.Attributes[new("omittedOptions")].Value.ShouldBe("timeZone,name");
        edited.Attributes[new("custom")].Value.ShouldBe("replaced");
        edited.Ports.ShouldBeSameAs(original.Ports);
        original.Options[0].Choices.ShouldHaveSingleItem().Value.Value.ShouldBe("slow");
        original.Attributes[new("omittedOptions")].Value.ShouldBe("name");
        original.Attributes[new("custom")].Value.ShouldBe("retained");
        Should.Throw<InvalidOperationException>(() => edited.WithOptionChoice("mode", " fast "))
            .Message.ShouldContain("already registered");
        edited.Options[0].Choices.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("option")]
    [InlineData("choice")]
    [InlineData("resource")]
    [InlineData("port")]
    public void Missing_presentation_targets_fail_without_adding_structure(string kind)
    {
        var original = CreateMetadata();
        var edits = new Dictionary<string, Action>
        {
            ["option"] = () => original.WithOption("missing", OptionValueKind.Text),
            ["choice"] = () => original.WithOptionChoice("missing", "value"),
            ["resource"] = () => original.WithResource("missing", "Missing"),
            ["port"] = () => original.WithPort("missing", PortDirection.Output)
        };
        Should.Throw<InvalidOperationException>(edits[kind]).Message.ShouldContain("missing");
        original.Options.Select(option => option.Name.Value).ShouldBe(["mode", "other"]);
        original.Resources.Select(resource => resource.Name.Value).ShouldBe(["store", "clock"]);
        original.Ports.Select(port => port.Name.Value).ShouldBe(["shared", "shared", "cancel"]);
    }

    [Theory]
    [InlineData("display")]
    [InlineData("attribute")]
    [InlineData("option")]
    [InlineData("choice")]
    [InlineData("resource")]
    [InlineData("port")]
    public void Presentation_edits_reject_null_metadata(string kind)
    {
        var edits = new Dictionary<string, Action>
        {
            ["display"] = () => MetadataPresentationExtensions.WithDisplay(null!, "label"),
            ["attribute"] = () => MetadataPresentationExtensions.WithAttribute(null!, "key", "value"),
            ["option"] = () => OptionPresentationExtensions.WithOption(null!, "mode", OptionValueKind.Text),
            ["choice"] = () => OptionPresentationExtensions.WithOptionChoice(null!, "mode", "fast"),
            ["resource"] = () => ResourcePresentationExtensions.WithResource(null!, "store", "Storage"),
            ["port"] = () => PortPresentationExtensions.WithPort(null!, "shared", PortDirection.Input)
        };
        Should.Throw<ArgumentNullException>(edits[kind]).ParamName.ShouldBe("metadata");
    }

    private static ComponentDesignMetadata CreateMetadata()
        => new()
        {
            Type = new("sample.presentation"), DisplayName = new("Original"),
            ProcessingCapabilities = CompositionProcessingCapabilities.ParallelRelaxedOrder,
            Options =
            [
                new()
                {
                    Name = new("mode"), Kind = OptionValueKind.Text, IsRequired = true,
                    Choices = [new() { Value = new("slow") }],
                    Attributes = Attributes()
                },
                new() { Name = new("other"), Kind = OptionValueKind.Boolean }
            ],
            Resources =
            [
                new() { Name = new("store"), IsRequired = true, Order = 2, ValueType = new("OriginalStore"), Attributes = Attributes() },
                new() { Name = new("clock") }
            ],
            Ports =
            [
                new()
                {
                    Name = new("shared"), Direction = PortDirection.Input, MessageType = typeof(FlowMessage),
                    ValueType = new("Request"), EventShapes = [new() { Type = "sample.changed", Version = 2 }],
                    Order = 3, LinkCardinality = ComponentPortLinkCardinality.Single,
                    InputShape = FlowValueShape.Any("Input"), Attributes = Attributes()
                },
                new() { Name = new("shared"), Direction = PortDirection.Output, MessageType = typeof(FlowMessage) },
                new() { Name = new("cancel"), Direction = PortDirection.Input, Kind = ComponentPortKind.Signal, MessageType = typeof(FlowMessage), DisplayName = new("Cancel") }
            ],
            Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
            {
                [new("omittedOptions")] = new("name"), [new("custom")] = new("retained")
            }
        };

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Attributes()
        => new Dictionary<ComponentAttributeName, ComponentAttributeValue> { [new("custom")] = new("retained") };
}
