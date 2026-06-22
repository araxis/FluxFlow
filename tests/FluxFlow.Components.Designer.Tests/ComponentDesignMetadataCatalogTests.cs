using FluxFlow.Components.Designer.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentDesignMetadataCatalogTests
{
    [Fact]
    public void Add_registers_and_finds_metadata_by_component_type()
    {
        var metadata = CreateMetadata();

        var catalog = new ComponentDesignMetadataCatalog().Add(metadata);

        catalog.TryGet(new ComponentType("sample.transform"), out var found).ShouldBeTrue();
        found.ShouldBeSameAs(metadata);
        found.Options[0].Kind.ShouldBe(OptionValueKind.Expression);
        found.Ports.Select(port => port.Name.Value).ShouldBe(["Input", "Output"]);
    }

    [Fact]
    public void Add_rejects_duplicate_component_type()
    {
        var catalog = new ComponentDesignMetadataCatalog().Add(CreateMetadata());

        var act = () => catalog.Add(CreateMetadata());

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain("already registered");
    }

    [Fact]
    public void FromProviders_loads_provider_metadata()
    {
        var first = CreateMetadata("sample.one");
        var second = CreateMetadata("sample.two");
        var provider = new ComponentDesignMetadataModule([first, second]);

        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(2);
        catalog.TryGet(first.Type, out _).ShouldBeTrue();
        catalog.TryGet(second.Type, out _).ShouldBeTrue();
    }

    [Fact]
    public void Validator_reports_invalid_metadata_shape()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            DisplayName = " ",
            SuggestedEditorWidth = 0,
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = "mode",
                    Kind = OptionValueKind.Enum,
                    Min = 2,
                    Max = 1,
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = "fast" },
                        new OptionChoiceMetadata { Value = "fast" }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = "mode",
                    Kind = OptionValueKind.Text
                }
            ],
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = default,
                    Direction = PortDirection.Input
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.Select(error => error.Path).ShouldContain(nameof(ComponentDesignMetadata.DisplayName));
        errors.Select(error => error.Path).ShouldContain(nameof(ComponentDesignMetadata.SuggestedEditorWidth));
        errors.ShouldContain(error => error.Message.Contains("Port name", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Message.Contains("minimum", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Message.Contains("Choice value", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Message.Contains("Option name", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Message.Contains("Port", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_default_component_type()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = default
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error => error.Path == nameof(ComponentDesignMetadata.Type));
    }

    [Fact]
    public void Option_metadata_supports_all_expected_value_kinds()
    {
        var kinds = Enum.GetValues<OptionValueKind>();

        kinds.ShouldBe([
            OptionValueKind.Text,
            OptionValueKind.Number,
            OptionValueKind.Boolean,
            OptionValueKind.Enum,
            OptionValueKind.MultilineText,
            OptionValueKind.Json,
            OptionValueKind.Expression,
            OptionValueKind.Duration,
            OptionValueKind.Secret
        ]);
    }

    [Fact]
    public void ComponentType_validates_value_and_preserves_identity()
    {
        var first = new ComponentType("flow.mapper");
        var second = new ComponentType("flow.mapper");

        first.ShouldBe(second);
        first.Value.ShouldBe("flow.mapper");
        first.ToString().ShouldBe("flow.mapper");
        new ComponentType("flow.filter").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentType_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentType(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component type cannot be empty");
    }

    [Fact]
    public void ComponentPortName_validates_value_and_preserves_identity()
    {
        var first = new ComponentPortName("Input");
        var second = new ComponentPortName("Input");

        first.ShouldBe(second);
        first.Value.ShouldBe("Input");
        first.ToString().ShouldBe("Input");
        new ComponentPortName("Output").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("node.port")]
    public void ComponentPortName_rejects_invalid_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentPortName(value);
        };

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Port_metadata_preserves_ordering_grouping_and_type_hints()
    {
        var ports = CreateMetadata().Ports.OrderBy(port => port.Order).ToArray();

        ports[0].Name.ShouldBe(new ComponentPortName("Input"));
        ports[0].Group.ShouldBe("Messages");
        ports[0].ValueType.ShouldBe("SampleInput");
        ports[0].IsPrimary.ShouldBeTrue();
        ports[1].Name.ShouldBe(new ComponentPortName("Output"));
        ports[1].Direction.ShouldBe(PortDirection.Output);
    }

    private static ComponentDesignMetadata CreateMetadata(string type = "sample.transform") => new()
    {
        Type = new ComponentType(type),
        DisplayName = "Sample Transform",
        Category = "Samples",
        Summary = "Transforms sample values.",
        IconKey = "transform",
        PreferredNodeName = "transform",
        SuggestedEditorWidth = 420,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "expression",
                Kind = OptionValueKind.Expression,
                DisplayName = "Expression",
                HelperText = "Expression evaluated for each input.",
                IsRequired = true
            },
            new OptionDesignMetadata
            {
                Name = "mode",
                Kind = OptionValueKind.Enum,
                DefaultValue = "strict",
                Choices =
                [
                    new OptionChoiceMetadata { Value = "strict", DisplayName = "Strict" },
                    new OptionChoiceMetadata { Value = "relaxed", DisplayName = "Relaxed" }
                ]
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName("Input"),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Input message.",
                ValueType = "SampleInput",
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName("Output"),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Messages",
                Order = 1,
                Summary = "Mapped message.",
                ValueType = "SampleOutput",
                IsPrimary = true
            }
        ],
        Attributes = new Dictionary<string, string>
        {
            ["shape"] = "transform"
        }
    };
}
