using FluxFlow.Components.Designer.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentDesignMetadataCatalogTests
{
    [Fact]
    public void Metadata_builder_creates_valid_metadata_with_options_resources_ports_and_attributes()
    {
        var metadata = new ComponentDesignMetadataBuilder("sample.builder")
            .WithDisplay(
                displayName: "Sample Builder",
                category: "Samples",
                summary: "Builds sample metadata.",
                iconKey: "sample",
                preferredNodeName: "sample",
                suggestedEditorWidth: 360)
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Evaluated for each input.",
                isRequired: true)
            .AddEnumOption(
                "mode",
                ["strict", "relaxed"],
                defaultValue: "strict")
            .AddResource(
                "engine",
                displayName: "Engine",
                order: 0,
                summary: "Expression engine.",
                valueType: "IExpressionEngine",
                isRequired: true)
            .AddInputPort(
                "Input",
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Input message.",
                valueType: "SampleInput",
                isPrimary: true)
            .AddOutputPort(
                "Output",
                displayName: "Output",
                group: "Messages",
                order: 0,
                summary: "Output message.",
                valueType: "SampleOutput",
                isPrimary: true)
            .AddAttribute("shape", "transform")
            .Build();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType("sample.builder"));
        metadata.DisplayName.ShouldBe("Sample Builder");
        metadata.Category.ShouldBe(new ComponentCategory("Samples"));
        metadata.Summary.ShouldBe("Builds sample metadata.");
        metadata.IconKey.ShouldBe(new ComponentIconKey("sample"));
        metadata.PreferredNodeName.ShouldBe("sample");
        metadata.SuggestedEditorWidth.ShouldBe(360);
        metadata.Options.Select(option => option.Name.Value).ShouldBe(["expression", "mode"]);
        metadata.Options[1].Choices.Select(choice => choice.Value).ShouldBe(["strict", "relaxed"]);
        metadata.Resources.ShouldHaveSingleItem().Name.ShouldBe(new ComponentResourceName("engine"));
        metadata.Ports.Select(port => port.Name.Value).ShouldBe(["Input", "Output"]);
        metadata.Attributes["shape"].ShouldBe("transform");
    }

    [Fact]
    public void Metadata_builder_output_can_be_registered_in_catalog()
    {
        var metadata = new ComponentDesignMetadataBuilder("sample.catalog")
            .AddInputPort("Input", order: 0)
            .AddOutputPort("Output", order: 0)
            .Build();

        var catalog = new ComponentDesignMetadataCatalog().Add(metadata);

        catalog.TryGet(new ComponentType("sample.catalog"), out var found).ShouldBeTrue();
        found.ShouldNotBeSameAs(metadata);
        found.Ports.Select(port => port.Name.Value).ShouldBe(["Input", "Output"]);
    }

    [Fact]
    public void Metadata_builder_snapshots_mutable_inputs_and_build_results()
    {
        var optionAttributes = new Dictionary<string, string>
        {
            ["scope"] = "editable"
        };
        var resourceAttributes = new Dictionary<string, string>
        {
            ["resource"] = "host-owned"
        };
        var portAttributes = new Dictionary<string, string>
        {
            ["side"] = "input"
        };
        var builder = new ComponentDesignMetadataBuilder("sample.snapshot")
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                attributes: optionAttributes)
            .AddResource(
                "engine",
                attributes: resourceAttributes)
            .AddInputPort(
                "Input",
                attributes: portAttributes)
            .AddAttribute("shape", "transform");

        var metadata = builder.Build();
        optionAttributes["scope"] = "changed";
        resourceAttributes["resource"] = "changed";
        portAttributes["side"] = "changed";
        builder
            .AddOption("enabled", OptionValueKind.Boolean)
            .AddOutputPort("Output");

        metadata.Options.Select(option => option.Name.Value).ShouldBe(["expression"]);
        metadata.Options[0].Attributes["scope"].ShouldBe("editable");
        metadata.Resources.ShouldHaveSingleItem().Attributes["resource"].ShouldBe("host-owned");
        metadata.Ports.ShouldHaveSingleItem().Attributes["side"].ShouldBe("input");
        metadata.Attributes["shape"].ShouldBe("transform");
    }

    [Fact]
    public void Metadata_builder_adds_attribute_ranges_and_snapshots_inputs()
    {
        var attributes = new Dictionary<string, string>
        {
            ["shape"] = "transform",
            ["domain"] = "sample"
        };

        var metadata = new ComponentDesignMetadataBuilder("sample.attributes")
            .AddAttributes(attributes)
            .Build();

        attributes["shape"] = "changed";
        attributes["later"] = "ignored";

        metadata.Attributes.Count.ShouldBe(2);
        metadata.Attributes["shape"].ShouldBe("transform");
        metadata.Attributes["domain"].ShouldBe("sample");
        metadata.Attributes.ContainsKey("later").ShouldBeFalse();
    }

    [Fact]
    public void Metadata_builder_uses_existing_validation()
    {
        var builder = new ComponentDesignMetadataBuilder("sample.invalid")
            .WithDisplay(suggestedEditorWidth: 0);

        var exception = Should.Throw<InvalidOperationException>(() => builder.Build());

        exception.Message.ShouldContain(nameof(ComponentDesignMetadata.SuggestedEditorWidth));
    }

    [Fact]
    public void Metadata_builder_rejects_null_nested_metadata()
    {
        var builder = new ComponentDesignMetadataBuilder("sample.invalid");

        Should.Throw<ArgumentNullException>(() => builder.AddOption(null!));
        Should.Throw<ArgumentNullException>(() => builder.AddResource(null!));
        Should.Throw<ArgumentNullException>(() => builder.AddPort(null!));
    }

    [Fact]
    public void Metadata_builder_rejects_null_fluent_primitive_arguments()
    {
        var builder = new ComponentDesignMetadataBuilder("sample.invalid");

        Should.Throw<ArgumentNullException>(() =>
            builder.AddOption((string)null!, OptionValueKind.Text))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            builder.AddEnumOption((string)null!, ["strict"]))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            builder.AddEnumOption("mode", [null!]))
            .ParamName.ShouldBe("choice");
        Should.Throw<ArgumentNullException>(() =>
            builder.AddResource((string)null!))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            builder.AddInputPort(null!))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            builder.AddOutputPort(null!))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            builder.AddPort((string)null!, PortDirection.Input))
            .ParamName.ShouldBe("name");
    }

    [Fact]
    public void Metadata_builder_rejects_invalid_attribute_arguments()
    {
        var builder = new ComponentDesignMetadataBuilder("sample.invalid");

        Should.Throw<ArgumentNullException>(() => builder.AddAttribute(null!, "value"))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentNullException>(() => builder.AddAttribute("shape", null!))
            .ParamName.ShouldBe("value");
        Should.Throw<ArgumentNullException>(() => builder.AddAttributes(null!))
            .ParamName.ShouldBe("attributes");
        Should.Throw<ArgumentNullException>(() => builder.AddAttributes(
            [
                new KeyValuePair<string, string>("shape", null!)
            ]))
            .ParamName.ShouldBe("value");
    }

    [Fact]
    public void Metadata_builder_rejects_duplicate_attributes()
    {
        var builder = new ComponentDesignMetadataBuilder("sample.invalid")
            .AddAttribute("shape", "transform");

        Should.Throw<ArgumentException>(() => builder.AddAttribute("shape", "source"));
    }

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
                Name = new ComponentOptionName("mode"),
                Kind = OptionValueKind.Enum,
                Choices = choices,
                Attributes = optionAttributes
            }
        };
        var resources = new List<ResourceDesignMetadata>
        {
            new()
            {
                Name = new ComponentResourceName("engine"),
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
    public void ServiceCollection_helpers_register_provider_and_catalog()
    {
        var services = new ServiceCollection()
            .AddComponentDesignMetadataProvider<TestMetadataProvider>()
            .AddComponentDesignMetadataCatalog();

        using var provider = services.BuildServiceProvider();

        var registeredProvider = provider
            .GetServices<IComponentDesignMetadataProvider>()
            .ShouldHaveSingleItem()
            .ShouldBeOfType<TestMetadataProvider>();
        registeredProvider.GetMetadata().ShouldHaveSingleItem()
            .Type.ShouldBe(new ComponentType("sample.service"));

        var catalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();
        catalog.TryGet(new ComponentType("sample.service"), out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull();
    }

    [Fact]
    public void ServiceCollection_helpers_skip_duplicate_provider_types()
    {
        var services = new ServiceCollection()
            .AddComponentDesignMetadataProvider<TestMetadataProvider>()
            .AddComponentDesignMetadataProvider<TestMetadataProvider>()
            .AddComponentDesignMetadataCatalog()
            .AddComponentDesignMetadataCatalog();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IComponentDesignMetadataProvider>()
            .ShouldHaveSingleItem()
            .ShouldBeOfType<TestMetadataProvider>();
        provider.GetServices<ComponentDesignMetadataCatalog>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void ServiceCollection_helpers_register_provider_instance()
    {
        var metadataProvider = new InstanceMetadataProvider("sample.instance");
        var services = new ServiceCollection()
            .AddComponentDesignMetadataProvider(metadataProvider)
            .AddComponentDesignMetadataProvider(metadataProvider)
            .AddComponentDesignMetadataCatalog();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IComponentDesignMetadataProvider>()
            .ShouldHaveSingleItem()
            .ShouldBeSameAs(metadataProvider);
        provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .TryGet(new ComponentType("sample.instance"), out _)
            .ShouldBeTrue();
    }

    [Fact]
    public void ServiceCollection_helpers_reject_invalid_arguments()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            ComponentDesignMetadataServiceCollectionExtensions
                .AddComponentDesignMetadataProvider<TestMetadataProvider>(null!))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            ComponentDesignMetadataServiceCollectionExtensions
                .AddComponentDesignMetadataProvider(null!, new TestMetadataProvider()))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            services.AddComponentDesignMetadataProvider(null!))
            .ParamName.ShouldBe("provider");
        Should.Throw<ArgumentNullException>(() =>
            ComponentDesignMetadataServiceCollectionExtensions
                .AddComponentDesignMetadataCatalog(null!))
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void MetadataModule_validates_and_snapshots_metadata()
    {
        var attributes = new Dictionary<string, string>
        {
            ["shape"] = "transform"
        };
        var optionAttributes = new Dictionary<string, string>
        {
            ["scope"] = "editable"
        };
        var metadata = CreateMetadata("sample.module") with
        {
            Attributes = attributes,
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("expression"),
                    Kind = OptionValueKind.Expression,
                    Attributes = optionAttributes
                }
            ]
        };

        var module = new ComponentDesignMetadataModule([metadata]);
        attributes["shape"] = "changed";
        optionAttributes["scope"] = "changed";

        var stored = module.GetMetadata().ShouldHaveSingleItem();

        stored.ShouldNotBeSameAs(metadata);
        stored.Attributes["shape"].ShouldBe("transform");
        stored.Options.ShouldHaveSingleItem().Attributes["scope"].ShouldBe("editable");
    }

    [Fact]
    public void MetadataModule_rejects_duplicate_component_types()
    {
        var act = () => new ComponentDesignMetadataModule([CreateMetadata(), CreateMetadata()]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain("already registered");
    }

    [Fact]
    public void FromProviders_rejects_null_provider_metadata_collection()
    {
        var provider = new NullMetadataProvider();

        var act = () => ComponentDesignMetadataCatalog.FromProviders([provider]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain(nameof(NullMetadataProvider));
    }

    [Fact]
    public void Validator_reports_invalid_metadata_shape()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            DisplayName = " ",
            Category = default(ComponentCategory),
            IconKey = default(ComponentIconKey),
            SuggestedEditorWidth = 0,
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("mode"),
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
                    Name = new ComponentOptionName("mode"),
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
        errors.Select(error => error.Path).ShouldContain(nameof(ComponentDesignMetadata.Category));
        errors.Select(error => error.Path).ShouldContain(nameof(ComponentDesignMetadata.IconKey));
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
                    Name = new ComponentOptionName("mode"),
                    Kind = OptionValueKind.Enum,
                    Choices = null!,
                    Attributes = null!
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("level"),
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
                    Name = new ComponentResourceName("engine"),
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
    public void Validator_reports_duplicate_primary_ports_per_direction()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input,
                    IsPrimary = true
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("AlternativeInput"),
                    Direction = PortDirection.Input,
                    IsPrimary = true
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Output"),
                    Direction = PortDirection.Output,
                    IsPrimary = true
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("AlternativeOutput"),
                    Direction = PortDirection.Output,
                    IsPrimary = true
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[1].{nameof(PortDesignMetadata.IsPrimary)}" &&
            error.Message.Contains("Input", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[3].{nameof(PortDesignMetadata.IsPrimary)}" &&
            error.Message.Contains("Output", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_invalid_enum_contract_values()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("mode"),
                    Kind = (OptionValueKind)999
                }
            ],
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = (PortDirection)999
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0].{nameof(OptionDesignMetadata.Kind)}" &&
            error.Message.Contains("Option kind", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[0].{nameof(PortDesignMetadata.Direction)}" &&
            error.Message.Contains("Port direction", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_invalid_resource_and_port_orders()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("engine"),
                    Order = -1
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("clock"),
                    Order = 0
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("store"),
                    Order = 0
                }
            ],
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input,
                    Order = -1
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("AlternativeInput"),
                    Direction = PortDirection.Input,
                    Order = 0
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("AnotherInput"),
                    Direction = PortDirection.Input,
                    Order = 0
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Output"),
                    Direction = PortDirection.Output,
                    Order = 0
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("AlternativeOutput"),
                    Direction = PortDirection.Output,
                    Order = 0
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Resources)}[0].{nameof(ResourceDesignMetadata.Order)}" &&
            error.Message.Contains("negative", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Resources)}[2].{nameof(ResourceDesignMetadata.Order)}" &&
            error.Message.Contains("already used", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[0].{nameof(PortDesignMetadata.Order)}" &&
            error.Message.Contains("negative", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[2].{nameof(PortDesignMetadata.Order)}" &&
            error.Message.Contains("Input", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[4].{nameof(PortDesignMetadata.Order)}" &&
            error.Message.Contains("Output", StringComparison.Ordinal));
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
                    Name = new ComponentOptionName("mode"),
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
                    Name = new ComponentOptionName("expression"),
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
                    Name = new ComponentOptionName("count"),
                    Kind = OptionValueKind.Number,
                    DefaultValue = "1"
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("enabled"),
                    Kind = OptionValueKind.Boolean,
                    DefaultValue = "true"
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("delay"),
                    Kind = OptionValueKind.Duration,
                    DefaultValue = "00:00:01"
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("label"),
                    Kind = OptionValueKind.Text,
                    DefaultValue = 1
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("mode"),
                    Kind = OptionValueKind.Enum,
                    DefaultValue = 1,
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = "strict" }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("missingMode"),
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
                    Name = new ComponentOptionName("belowMin"),
                    Kind = OptionValueKind.Number,
                    DefaultValue = 0,
                    Min = 1
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("aboveMax"),
                    Kind = OptionValueKind.Number,
                    DefaultValue = 11,
                    Max = 10
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("durationBelowMin"),
                    Kind = OptionValueKind.Duration,
                    DefaultValue = TimeSpan.FromMilliseconds(500),
                    Min = 1
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("durationAboveMax"),
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
    public void Validator_reports_non_finite_number_bounds_and_default_values()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("nanMin"),
                    Kind = OptionValueKind.Number,
                    Min = double.NaN
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("infiniteMax"),
                    Kind = OptionValueKind.Number,
                    Max = double.PositiveInfinity
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("nanDefault"),
                    Kind = OptionValueKind.Number,
                    DefaultValue = double.NaN
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("infiniteDefault"),
                    Kind = OptionValueKind.Number,
                    DefaultValue = float.NegativeInfinity
                }
            ]
        };

        var errors = ComponentDesignMetadataValidator.Validate(metadata);

        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[0].{nameof(OptionDesignMetadata.Min)}" &&
            error.Message.Contains("finite", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[1].{nameof(OptionDesignMetadata.Max)}" &&
            error.Message.Contains("finite", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[2].{nameof(OptionDesignMetadata.DefaultValue)}" &&
            error.Message.Contains("finite", StringComparison.Ordinal));
        errors.ShouldContain(error =>
            error.Path == $"{nameof(ComponentDesignMetadata.Options)}[3].{nameof(OptionDesignMetadata.DefaultValue)}" &&
            error.Message.Contains("finite", StringComparison.Ordinal));
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
                    Name = new ComponentOptionName("label"),
                    Kind = OptionValueKind.Text,
                    DefaultValue = "value"
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("body"),
                    Kind = OptionValueKind.MultilineText,
                    DefaultValue = "line one"
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("expression"),
                    Kind = OptionValueKind.Expression,
                    DefaultValue = "$"
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("secret"),
                    Kind = OptionValueKind.Secret,
                    DefaultValue = "name"
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("count"),
                    Kind = OptionValueKind.Number,
                    DefaultValue = 1,
                    Min = 0,
                    Max = 10
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("enabled"),
                    Kind = OptionValueKind.Boolean,
                    DefaultValue = true
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("delay"),
                    Kind = OptionValueKind.Duration,
                    DefaultValue = TimeSpan.FromSeconds(1),
                    Min = 1
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("mode"),
                    Kind = OptionValueKind.Enum,
                    DefaultValue = "strict",
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = "strict" }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("enumMode"),
                    Kind = OptionValueKind.Enum,
                    DefaultValue = SampleMode.Relaxed,
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = nameof(SampleMode.Relaxed) }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("json"),
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
                    Name = new ComponentOptionName("label"),
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
                    Name = default,
                    DisplayName = " ",
                    Attributes = new Dictionary<string, string>
                    {
                        [""] = "resource"
                    }
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("engine")
                },
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("engine")
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
    public void ComponentCategory_validates_value_and_preserves_identity()
    {
        var first = new ComponentCategory("Mapping");
        var second = new ComponentCategory("Mapping");

        first.ShouldBe(second);
        first.Value.ShouldBe("Mapping");
        first.ToString().ShouldBe("Mapping");
        new ComponentCategory("Control").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentCategory_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentCategory(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component category cannot be empty");
    }

    [Fact]
    public void ComponentIconKey_validates_value_and_preserves_identity()
    {
        var first = new ComponentIconKey("transform");
        var second = new ComponentIconKey("transform");

        first.ShouldBe(second);
        first.Value.ShouldBe("transform");
        first.ToString().ShouldBe("transform");
        new ComponentIconKey("source").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentIconKey_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentIconKey(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component icon key cannot be empty");
    }

    [Fact]
    public void ComponentOptionName_validates_value_and_preserves_identity()
    {
        var first = new ComponentOptionName("expression");
        var second = new ComponentOptionName("expression");

        first.ShouldBe(second);
        first.Value.ShouldBe("expression");
        first.ToString().ShouldBe("expression");
        new ComponentOptionName("mode").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentOptionName_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentOptionName(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component option name cannot be empty");
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

        resources[0].Name.ShouldBe(new ComponentResourceName("engine"));
        resources[0].DisplayName.ShouldBe("Engine");
        resources[0].ValueType.ShouldBe("IExpressionEngine");
        resources[0].IsRequired.ShouldBeTrue();
        resources[1].Name.ShouldBe(new ComponentResourceName("clock"));
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
    public void ComponentResourceName_validates_value_and_preserves_identity()
    {
        var first = new ComponentResourceName("engine");
        var second = new ComponentResourceName("engine");

        first.ShouldBe(second);
        first.Value.ShouldBe("engine");
        first.ToString().ShouldBe("engine");
        new ComponentResourceName("clock").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentResourceName_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentResourceName(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component resource name cannot be empty");
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
        Category = new ComponentCategory("Samples"),
        Summary = "Transforms sample values.",
        IconKey = new ComponentIconKey("transform"),
        PreferredNodeName = "transform",
        SuggestedEditorWidth = 420,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = new ComponentOptionName("expression"),
                Kind = OptionValueKind.Expression,
                DisplayName = "Expression",
                HelperText = "Expression evaluated for each input.",
                IsRequired = true
            },
            new OptionDesignMetadata
            {
                Name = new ComponentOptionName("mode"),
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
                Name = new ComponentResourceName("engine"),
                DisplayName = "Engine",
                Order = 0,
                Summary = "Expression engine resource.",
                ValueType = "IExpressionEngine",
                IsRequired = true
            },
            new ResourceDesignMetadata
            {
                Name = new ComponentResourceName("clock"),
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

    private sealed class NullMetadataProvider : IComponentDesignMetadataProvider
    {
        public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() => null!;
    }

    private sealed class TestMetadataProvider : IComponentDesignMetadataProvider
    {
        public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
            => [CreateMetadata("sample.service")];
    }

    private sealed class InstanceMetadataProvider(string type) : IComponentDesignMetadataProvider
    {
        public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
            => [CreateMetadata(type)];
    }

    private enum SampleMode
    {
        Relaxed
    }
}
