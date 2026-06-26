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
        found.ShouldNotBeSameAs(metadata);
        found.Options[0].Kind.ShouldBe(OptionValueKind.Expression);
        found.Ports.Select(port => port.Name.Value).ShouldBe(["Input", "Output"]);
    }

    [Fact]
    public void Add_snapshots_registered_metadata()
    {
        var metadataAttributes = new Dictionary<string, string>
        {
            ["shape"] = "transform"
        };
        var optionAttributes = new Dictionary<string, string>
        {
            ["scope"] = "editable"
        };
        var choiceAttributes = new Dictionary<string, string>
        {
            ["kind"] = "mode"
        };
        var resourceAttributes = new Dictionary<string, string>
        {
            ["resource"] = "host-owned"
        };
        var portAttributes = new Dictionary<string, string>
        {
            ["side"] = "input"
        };
        var choices = new List<OptionChoiceMetadata>
        {
            new()
            {
                Value = "strict",
                Attributes = choiceAttributes
            }
        };
        var options = new List<OptionDesignMetadata>
        {
            new()
            {
                Name = "mode",
                Kind = OptionValueKind.Enum,
                Choices = choices,
                Attributes = optionAttributes
            }
        };
        var resources = new List<ResourceDesignMetadata>
        {
            new()
            {
                Name = "engine",
                Attributes = resourceAttributes
            }
        };
        var ports = new List<PortDesignMetadata>
        {
            new()
            {
                Name = new ComponentPortName("Input"),
                Direction = PortDirection.Input,
                Attributes = portAttributes
            }
        };
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.snapshot"),
            Options = options,
            Resources = resources,
            Ports = ports,
            Attributes = metadataAttributes
        };

        var catalog = new ComponentDesignMetadataCatalog().Add(metadata);
        options.Clear();
        choices.Clear();
        resources.Clear();
        ports.Clear();
        metadataAttributes["shape"] = "changed";
        optionAttributes["scope"] = "changed";
        choiceAttributes["kind"] = "changed";
        resourceAttributes["resource"] = "changed";
        portAttributes["side"] = "changed";

        catalog.TryGet(metadata.Type, out var found).ShouldBeTrue();

        found.Options.ShouldHaveSingleItem().Attributes["scope"].ShouldBe("editable");
        found.Options[0].Choices.ShouldHaveSingleItem().Attributes["kind"].ShouldBe("mode");
        found.Resources.ShouldHaveSingleItem().Attributes["resource"].ShouldBe("host-owned");
        found.Ports.ShouldHaveSingleItem().Attributes["side"].ShouldBe("input");
        found.Attributes["shape"].ShouldBe("transform");
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
    public void Validator_reports_null_top_level_collections()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options = null!,
            Resources = null!,
            Ports = null!,
            Attributes = null!
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error => error.Path == nameof(ComponentDesignMetadata.Options));
        errors.ShouldContain(error => error.Path == nameof(ComponentDesignMetadata.Resources));
        errors.ShouldContain(error => error.Path == nameof(ComponentDesignMetadata.Ports));
        errors.ShouldContain(error => error.Path == nameof(ComponentDesignMetadata.Attributes));
    }

    [Fact]
    public void Validator_reports_null_nested_collection_items()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                null!,
                new OptionDesignMetadata
                {
                    Name = "mode",
                    Kind = OptionValueKind.Enum,
                    Choices = null!,
                    Attributes = null!
                },
                new OptionDesignMetadata
                {
                    Name = "level",
                    Kind = OptionValueKind.Enum,
                    Choices =
                    [
                        null!,
                        new OptionChoiceMetadata
                        {
                            Value = "strict",
                            Attributes = null!
                        }
                    ]
                }
            ],
            Resources =
            [
                null!,
                new ResourceDesignMetadata
                {
                    Name = "engine",
                    Attributes = null!
                }
            ],
            Ports =
            [
                null!,
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input,
                    Attributes = null!
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0]");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[1].{nameof(OptionDesignMetadata.Choices)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[1].{nameof(OptionDesignMetadata.Attributes)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[2].{nameof(OptionDesignMetadata.Choices)}[0]");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[2].{nameof(OptionDesignMetadata.Choices)}[1].{nameof(OptionChoiceMetadata.Attributes)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Resources)}[0]");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Resources)}[1].{nameof(ResourceDesignMetadata.Attributes)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[0]");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[1].{nameof(PortDesignMetadata.Attributes)}");
    }

    [Fact]
    public void Validator_reports_enum_option_without_choices()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = "mode",
                    Kind = OptionValueKind.Enum
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0].{nameof(OptionDesignMetadata.Choices)}" &&
            error.Message.Contains("Enum options", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_choices_on_non_enum_option()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = "expression",
                    Kind = OptionValueKind.Expression,
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = "value" }
                    ]
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0].{nameof(OptionDesignMetadata.Choices)}" &&
            error.Message.Contains("Only enum", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_option_default_value_mismatches()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = "count",
                    Kind = OptionValueKind.Number,
                    DefaultValue = "1"
                },
                new OptionDesignMetadata
                {
                    Name = "enabled",
                    Kind = OptionValueKind.Boolean,
                    DefaultValue = "true"
                },
                new OptionDesignMetadata
                {
                    Name = "delay",
                    Kind = OptionValueKind.Duration,
                    DefaultValue = "00:00:01"
                },
                new OptionDesignMetadata
                {
                    Name = "label",
                    Kind = OptionValueKind.Text,
                    DefaultValue = 1
                },
                new OptionDesignMetadata
                {
                    Name = "mode",
                    Kind = OptionValueKind.Enum,
                    DefaultValue = 1,
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = "strict" }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = "missingMode",
                    Kind = OptionValueKind.Enum,
                    DefaultValue = "missing",
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = "strict" }
                    ]
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0].{nameof(OptionDesignMetadata.DefaultValue)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[1].{nameof(OptionDesignMetadata.DefaultValue)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[2].{nameof(OptionDesignMetadata.DefaultValue)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[3].{nameof(OptionDesignMetadata.DefaultValue)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[4].{nameof(OptionDesignMetadata.DefaultValue)}");
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Options)}[5].{nameof(OptionDesignMetadata.DefaultValue)}");
    }

    [Fact]
    public void Validator_reports_number_and_duration_default_values_outside_range()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = "belowMin",
                    Kind = OptionValueKind.Number,
                    DefaultValue = 0,
                    Min = 1
                },
                new OptionDesignMetadata
                {
                    Name = "aboveMax",
                    Kind = OptionValueKind.Number,
                    DefaultValue = 11,
                    Max = 10
                },
                new OptionDesignMetadata
                {
                    Name = "durationBelowMin",
                    Kind = OptionValueKind.Duration,
                    DefaultValue = TimeSpan.FromMilliseconds(500),
                    Min = 1
                },
                new OptionDesignMetadata
                {
                    Name = "durationAboveMax",
                    Kind = OptionValueKind.Duration,
                    DefaultValue = TimeSpan.FromSeconds(2),
                    Max = 1
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0].{nameof(OptionDesignMetadata.DefaultValue)}" &&
            error.Message.Contains("minimum", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[1].{nameof(OptionDesignMetadata.DefaultValue)}" &&
            error.Message.Contains("maximum", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[2].{nameof(OptionDesignMetadata.DefaultValue)}" &&
            error.Message.Contains("minimum", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[3].{nameof(OptionDesignMetadata.DefaultValue)}" &&
            error.Message.Contains("maximum", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_accepts_option_default_values_that_match_kind()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.valid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = "label",
                    Kind = OptionValueKind.Text,
                    DefaultValue = "value"
                },
                new OptionDesignMetadata
                {
                    Name = "body",
                    Kind = OptionValueKind.MultilineText,
                    DefaultValue = "line one"
                },
                new OptionDesignMetadata
                {
                    Name = "expression",
                    Kind = OptionValueKind.Expression,
                    DefaultValue = "$"
                },
                new OptionDesignMetadata
                {
                    Name = "secret",
                    Kind = OptionValueKind.Secret,
                    DefaultValue = "name"
                },
                new OptionDesignMetadata
                {
                    Name = "count",
                    Kind = OptionValueKind.Number,
                    DefaultValue = 1,
                    Min = 0,
                    Max = 10
                },
                new OptionDesignMetadata
                {
                    Name = "enabled",
                    Kind = OptionValueKind.Boolean,
                    DefaultValue = true
                },
                new OptionDesignMetadata
                {
                    Name = "delay",
                    Kind = OptionValueKind.Duration,
                    DefaultValue = TimeSpan.FromSeconds(1),
                    Min = 1
                },
                new OptionDesignMetadata
                {
                    Name = "mode",
                    Kind = OptionValueKind.Enum,
                    DefaultValue = "strict",
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = "strict" }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = "enumMode",
                    Kind = OptionValueKind.Enum,
                    DefaultValue = SampleMode.Relaxed,
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = nameof(SampleMode.Relaxed) }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = "json",
                    Kind = OptionValueKind.Json,
                    DefaultValue = new Dictionary<string, string>
                    {
                        ["name"] = "value"
                    }
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validator_reports_min_max_on_non_numeric_options()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = "label",
                    Kind = OptionValueKind.Text,
                    Min = 1
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0].{nameof(OptionDesignMetadata.Min)}" &&
            error.Message.Contains("min/max", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_invalid_resource_metadata_shape()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = "",
                    DisplayName = " ",
                    Attributes = new Dictionary<string, string>
                    {
                        [""] = "resource"
                    }
                },
                new ResourceDesignMetadata
                {
                    Name = "engine"
                },
                new ResourceDesignMetadata
                {
                    Name = "engine"
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error => error.Message.Contains("Resource name", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Resources)}[0].{nameof(ResourceDesignMetadata.DisplayName)}");
        errors.ShouldContain(error => error.Message.Contains("already used", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Message.Contains("Attribute keys", StringComparison.Ordinal));
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

    [Fact]
    public void Resource_metadata_preserves_ordering_required_flag_and_type_hints()
    {
        var resources = CreateMetadata().Resources.OrderBy(resource => resource.Order).ToArray();

        resources[0].Name.ShouldBe("engine");
        resources[0].DisplayName.ShouldBe("Engine");
        resources[0].ValueType.ShouldBe("IExpressionEngine");
        resources[0].IsRequired.ShouldBeTrue();
        resources[1].Name.ShouldBe("clock");
        resources[1].IsRequired.ShouldBeFalse();
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
        Resources =
        [
            new ResourceDesignMetadata
            {
                Name = "engine",
                DisplayName = "Engine",
                Order = 0,
                Summary = "Expression engine resource.",
                ValueType = "IExpressionEngine",
                IsRequired = true
            },
            new ResourceDesignMetadata
            {
                Name = "clock",
                DisplayName = "Clock",
                Order = 1,
                Summary = "Optional clock resource.",
                ValueType = nameof(TimeProvider)
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

    private enum SampleMode
    {
        Relaxed
    }
}
