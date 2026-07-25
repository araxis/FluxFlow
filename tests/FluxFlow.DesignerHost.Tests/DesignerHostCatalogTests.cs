using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Timers.Composition;
using Shouldly;
using Xunit;

namespace FluxFlow.DesignerHost.Tests;

public sealed class DesignerHostCatalogTests
{
    [Fact]
    public void Palette_projects_display_metadata_and_ports()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            DisplayName = new ComponentMetadataText("Widget"),
            Category = new ComponentCategory("Sample"),
            Summary = new ComponentMetadataText("Does widget things."),
            IconKey = new ComponentIconKey("widget"),
            PreferredNodeName = new ComponentPreferredNodeName("widget"),
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Output"),
                    Direction = PortDirection.Output,
                    Order = 1,
                    ValueType = new ComponentValueTypeHint("string"),
                    IsPrimary = true
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input,
                    Order = 0,
                    IsPrimary = true
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Ack"),
                    Direction = PortDirection.Input,
                    Order = 1,
                    Attributes = PortDesignMetadataAttributes.CreateSignalMap()
                }
            ]
        });

        var item = catalog.CreatePaletteItems().ShouldHaveSingleItem();

        item.ComponentType.ShouldBe("sample.widget");
        item.DisplayName.ShouldBe("Widget");
        item.Category.ShouldBe("Sample");
        item.Summary.ShouldBe("Does widget things.");
        item.IconKey.ShouldBe("widget");
        item.PreferredNodeName.ShouldBe("widget");
        var input = item.Inputs.ShouldHaveSingleItem();
        input.Name.ShouldBe("Input");
        input.Kind.ShouldBe(PortKind.Input);
        var signal = item.SignalInputs.ShouldHaveSingleItem();
        signal.Name.ShouldBe("Ack");
        signal.Kind.ShouldBe(PortKind.SignalInput);
        var output = item.Outputs.Single(port => port.Name == "Output");
        output.Name.ShouldBe("Output");
        output.Kind.ShouldBe(PortKind.Output);
        output.ValueType.ShouldBe("string");
        output.IsPrimary.ShouldBeTrue();
        item.Outputs.ShouldContain(port => port.Name == "Events");
    }

    [Fact]
    public void Palette_falls_back_to_type_and_general_category()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.bare")
        });

        var item = catalog.CreatePaletteItems().ShouldHaveSingleItem();

        item.DisplayName.ShouldBe("sample.bare");
        item.Category.ShouldBe(DesignerHostCatalog.DefaultCategory);
        item.Summary.ShouldBeNull();
        item.Inputs.ShouldBeEmpty();
        item.SignalInputs.ShouldBeEmpty();
        item.Outputs.Select(port => port.Name).ShouldBe(["Events"]);
    }

    [Fact]
    public void Palette_orders_by_category_then_display_name()
    {
        var catalog = CreateHostCatalog(
            new ComponentDesignMetadata
            {
                Type = new ComponentType("sample.zeta"),
                DisplayName = new ComponentMetadataText("Zeta"),
                Category = new ComponentCategory("Alpha")
            },
            new ComponentDesignMetadata
            {
                Type = new ComponentType("sample.alpha"),
                DisplayName = new ComponentMetadataText("Alpha"),
                Category = new ComponentCategory("Beta")
            },
            new ComponentDesignMetadata
            {
                Type = new ComponentType("sample.mid"),
                DisplayName = new ComponentMetadataText("Mid"),
                Category = new ComponentCategory("Alpha")
            });

        var items = catalog.CreatePaletteItems();

        items.Select(item => item.ComponentType)
            .ShouldBe(["sample.mid", "sample.zeta", "sample.alpha"]);
    }

    [Fact]
    public void Inspector_groups_sections_in_first_appearance_order_with_primary_before_advanced()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            Options =
            [
                CreateOption("timeout", OptionValueKind.Duration, section: "Timing"),
                CreateOption("retries", OptionValueKind.Number, section: "Behavior"),
                CreateOption(
                    "jitter",
                    OptionValueKind.Number,
                    section: "Timing",
                    importance: OptionDesignMetadataAttributeValues.Advanced),
                CreateOption("interval", OptionValueKind.Duration, section: "Timing")
            ]
        });

        var inspector = catalog.CreateInspector("sample.widget").ShouldNotBeNull();

        inspector.Sections.Select(section => section.Name).ShouldBe(["Timing", "Behavior", "Runtime"]);
        inspector.Sections[0].Options.Select(option => option.Name)
            .ShouldBe(["timeout", "interval", "jitter"]);
        inspector.Sections[0].Options[2].IsAdvanced.ShouldBeTrue();
    }

    [Fact]
    public void Inspector_defaults_missing_section_to_general()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            Options = [CreateOption("label", OptionValueKind.Text)]
        });

        var inspector = catalog.CreateInspector("sample.widget").ShouldNotBeNull();

        var section = inspector.Sections.Single(section => section.Name == DesignerHostCatalog.DefaultSection);
        section.Name.ShouldBe(DesignerHostCatalog.DefaultSection);
    }

    [Fact]
    public void Inspector_returns_null_for_unknown_component()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget")
        });

        catalog.CreateInspector("sample.unknown").ShouldBeNull();
    }

    [Fact]
    public void Editor_hint_wins_over_value_kind()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            Options =
            [
                CreateOption(
                    "predicate",
                    OptionValueKind.Text,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: "jsonata")
            ]
        });

        var option = FindOption(catalog, "sample.widget", "predicate");

        option.Editor.ShouldBe(OptionEditorKind.Expression);
        option.Syntax.ShouldBe("jsonata");
    }

    [Fact]
    public void Unknown_editor_hint_falls_back_to_value_kind()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            Options = [CreateOption("flag", OptionValueKind.Boolean, editor: "fancy-toggle")]
        });

        FindOption(catalog, "sample.widget", "flag").Editor.ShouldBe(OptionEditorKind.Toggle);
    }

    [Theory]
    [InlineData(OptionValueKind.Text, OptionEditorKind.Text)]
    [InlineData(OptionValueKind.Number, OptionEditorKind.Number)]
    [InlineData(OptionValueKind.Boolean, OptionEditorKind.Toggle)]
    [InlineData(OptionValueKind.MultilineText, OptionEditorKind.MultilineText)]
    [InlineData(OptionValueKind.Json, OptionEditorKind.Json)]
    [InlineData(OptionValueKind.Expression, OptionEditorKind.Expression)]
    [InlineData(OptionValueKind.Duration, OptionEditorKind.Duration)]
    [InlineData(OptionValueKind.Secret, OptionEditorKind.Secret)]
    public void Value_kinds_map_to_conservative_editors(
        OptionValueKind kind,
        OptionEditorKind expected)
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            Options = [CreateOption("value", kind)]
        });

        FindOption(catalog, "sample.widget", "value").Editor.ShouldBe(expected);
    }

    [Fact]
    public void Enum_options_project_select_editor_with_choices()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("mode"),
                    Kind = OptionValueKind.Enum,
                    Choices =
                    [
                        new OptionChoiceMetadata
                        {
                            Value = new ComponentOptionChoiceValue("fast"),
                            DisplayName = new ComponentMetadataText("Fast")
                        },
                        new OptionChoiceMetadata
                        {
                            Value = new ComponentOptionChoiceValue("safe")
                        }
                    ]
                }
            ]
        });

        var option = FindOption(catalog, "sample.widget", "mode");

        option.Editor.ShouldBe(OptionEditorKind.Select);
        option.Choices.Select(choice => choice.DisplayName).ShouldBe(["Fast", "safe"]);
    }

    [Fact]
    public void Resource_prompts_project_host_owned_hints_only()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget"),
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("store"),
                    DisplayName = new ComponentMetadataText("Store"),
                    Order = 0,
                    IsRequired = true,
                    ValueType = new ComponentValueTypeHint("IStorageStore"),
                    Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                        ResourceDesignMetadataAttributeValues.Store,
                        keyPattern: "store:{name}",
                        requiredWhenAnyOption: "storeName, fallbackStore")
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("internal"),
                    Order = 1
                }
            ]
        });

        var prompt = catalog.CreateResourcePrompts("sample.widget")
            .Single(prompt => prompt.ResourceName == "store");

        prompt.ResourceName.ShouldBe("store");
        prompt.DisplayName.ShouldBe("Store");
        prompt.PickerKind.ShouldBe(ResourceDesignMetadataAttributeValues.Store);
        prompt.KeyPattern.ShouldBe("store:{name}");
        prompt.ValueType.ShouldBe("IStorageStore");
        prompt.IsRequired.ShouldBeTrue();
        prompt.RequiredWhenAnyOptions.ShouldBe(["storeName", "fallbackStore"]);
    }

    [Fact]
    public void Resource_prompts_are_empty_for_unknown_component()
    {
        var catalog = CreateHostCatalog(new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.widget")
        });

        catalog.CreateResourcePrompts("sample.unknown").ShouldBeEmpty();
    }

    [Fact]
    public void Timers_provider_projects_palette_inspector_and_clock_prompt()
    {
        var catalog = new DesignerHostCatalog(
            ComponentDesignMetadataCatalog.FromProviders([new TimersComponentDesignMetadataProvider()]));

        var items = catalog.CreatePaletteItems();
        items.Count.ShouldBe(5);
        items.ShouldAllBe(item => item.DisplayName.Length > 0 && item.Category.Length > 0);

        var interval = catalog.CreateInspector(TimersCompositionNodeTypes.Interval).ShouldNotBeNull();
        interval.Sections.ShouldNotBeEmpty();
        interval.Sections
            .SelectMany(section => section.Options)
            .ShouldContain(option => option.Name == "interval");

        var clockPrompt = interval.ResourcePrompts
            .Single(prompt => prompt.ResourceName == "clock");
        clockPrompt.ResourceName.ShouldBe("clock");
        clockPrompt.PickerKind.ShouldBe(ResourceDesignMetadataAttributeValues.Clock);
    }

    private static DesignerHostCatalog CreateHostCatalog(params ComponentDesignMetadata[] metadata)
        => new(new ComponentDesignMetadataCatalog().AddRange(metadata));

    private static OptionEditorModel FindOption(
        DesignerHostCatalog catalog,
        string componentType,
        string optionName)
        => catalog.CreateInspector(componentType)
            .ShouldNotBeNull()
            .Sections
            .SelectMany(section => section.Options)
            .Single(option => option.Name == optionName);

    private static OptionDesignMetadata CreateOption(
        string name,
        OptionValueKind kind,
        string? section = null,
        string? importance = null,
        string? editor = null,
        string? syntax = null)
        => new()
        {
            Name = new ComponentOptionName(name),
            Kind = kind,
            Attributes = OptionDesignMetadataAttributes.CreateMap(
                section: section,
                importance: importance,
                editor: editor,
                syntax: syntax)
        };
}
