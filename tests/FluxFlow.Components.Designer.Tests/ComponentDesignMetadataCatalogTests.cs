using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentDesignMetadataCatalogTests
{
    [Fact]
    public void Explicit_metadata_can_be_registered_in_catalog()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.catalog"),
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input,
                    Order = 0
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Output"),
                    Direction = PortDirection.Output,
                    Order = 0
                }
            ]
        };

        var catalog = new ComponentDesignMetadataCatalog([metadata]);

        catalog.TryGet(new ComponentType("sample.catalog"), out var found).ShouldBeTrue();
        found.ShouldNotBeSameAs(metadata);
        found.Ports.Select(port => port.Name.Value).ShouldBe(["Input", "Output", "Events"]);
    }

    [Fact]
    public void Catalog_resolves_only_exact_component_types()
    {
        var metadata = new ComponentDesignMetadata { Type = new ComponentType("data.map") };
        var catalog = new ComponentDesignMetadataCatalog([metadata]);

        catalog.All.ShouldHaveSingleItem().Type.ShouldBe(new ComponentType("data.map"));
        catalog.TryGet(new ComponentType("data.map"), out _).ShouldBeTrue();
        catalog.TryGet(new ComponentType("flow.mapper"), out _).ShouldBeFalse();
    }

    [Fact]
    public void Catalog_adds_the_reserved_component_events_output()
    {
        var catalog = new ComponentDesignMetadataCatalog(
        [
            new ComponentDesignMetadata
            {
                Type = new ComponentType("data.map"),
                Ports =
                [
                    new PortDesignMetadata
                    {
                        Name = new ComponentPortName("Output"),
                        Direction = PortDirection.Output,
                        ValueType = new ComponentValueTypeHint("JsonElement"),
                        IsPrimary = true
                    }
                ]
            }
        ]);

        catalog.TryGet(new ComponentType("data.map"), out var metadata).ShouldBeTrue();
        var events = metadata.Ports.Single(port => port.Name.Value == "Events");
        events.Direction.ShouldBe(PortDirection.Output);
        events.ValueType.ShouldNotBeNull();
        events.ValueType.Value.Value.ShouldBe("ComponentEvent");
        events.Group.ShouldNotBeNull();
        events.Group.Value.Value.ShouldBe("Diagnostics");
        events.IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public void Catalog_preserves_capacity_and_replaces_only_identity_and_scheduling_options()
    {
        var catalog = new ComponentDesignMetadataCatalog(
        [
            new ComponentDesignMetadata
            {
                Type = new ComponentType("data.map"),
                Options =
                [
                    new OptionDesignMetadata
                    {
                        Name = new ComponentOptionName("name"),
                        Kind = OptionValueKind.Text
                    },
                    new OptionDesignMetadata
                    {
                        Name = new ComponentOptionName("boundedCapacity"),
                        Kind = OptionValueKind.Number
                    },
                    new OptionDesignMetadata
                    {
                        Name = new ComponentOptionName("maxDegreeOfParallelism"),
                        Kind = OptionValueKind.Number
                    },
                    new OptionDesignMetadata
                    {
                        Name = new ComponentOptionName("ensureOrdered"),
                        Kind = OptionValueKind.Boolean
                    },
                    new OptionDesignMetadata
                    {
                        Name = new ComponentOptionName("expression"),
                        Kind = OptionValueKind.Expression
                    }
                ]
            }
        ]);

        catalog.TryGet(new ComponentType("data.map"), out var metadata).ShouldBeTrue();
        metadata.Options.Select(static option => option.Name.Value)
            .ShouldBe(["boundedCapacity", "expression", "processing"]);
        var processing = metadata.Resources.Single(resource => resource.Name.Value == "processing");
        processing.Attributes[new ComponentAttributeName(ResourceDesignMetadataAttributeNames.PickerKind)]
            .Value.ShouldBe(ResourceDesignMetadataAttributeValues.ProcessingProfile);
        metadata.Attributes[new ComponentAttributeName("omittedOptions")].Value
            .ShouldBe("name,maxDegreeOfParallelism,ensureOrdered");
    }

    [Fact]
    public void Resource_metadata_attribute_helper_creates_host_owned_picker_hints()
    {
        var attributes = ResourceDesignMetadataAttributes.CreateHostOwned(
            ResourceDesignMetadataAttributeValues.Clock,
            keyPattern: "clock:{name}",
            option: "clockResource",
            requiredWhenAnyOption: "usesClock");

        attributes[ResourceDesignMetadataAttributeNames.Ownership]
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        attributes[ResourceDesignMetadataAttributeNames.PickerKind]
            .ShouldBe(ResourceDesignMetadataAttributeValues.Clock);
        attributes[ResourceDesignMetadataAttributeNames.KeyPattern]
            .ShouldBe("clock:{name}");
        attributes[ResourceDesignMetadataAttributeNames.Option]
            .ShouldBe("clockResource");
        attributes[ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption]
            .ShouldBe("usesClock");

        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.resource-hints"),
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("clock"),
                    Attributes = attributes.ToDictionary(
                        pair => new ComponentAttributeName(pair.Key),
                        pair => new ComponentAttributeValue(pair.Value))
                }
            ]
        };

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
    }

    [Fact]
    public void Resource_metadata_attribute_helper_creates_typed_attribute_map()
    {
        var attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
            ResourceDesignMetadataAttributeValues.Store);

        attributes[new ComponentAttributeName(ResourceDesignMetadataAttributeNames.Ownership)]
            .ShouldBe(new ComponentAttributeValue(ResourceDesignMetadataAttributeValues.HostOwned));
        attributes[new ComponentAttributeName(ResourceDesignMetadataAttributeNames.PickerKind)]
            .ShouldBe(new ComponentAttributeValue(ResourceDesignMetadataAttributeValues.Store));
    }

    [Fact]
    public void Option_metadata_attribute_helper_creates_editor_hints()
    {
        var attributes = OptionDesignMetadataAttributes.Create(
            section: "Mapping",
            importance: OptionDesignMetadataAttributeValues.Primary,
            editor: OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: "engine");

        attributes[OptionDesignMetadataAttributeNames.Section]
            .ShouldBe("Mapping");
        attributes[OptionDesignMetadataAttributeNames.Importance]
            .ShouldBe(OptionDesignMetadataAttributeValues.Primary);
        attributes[OptionDesignMetadataAttributeNames.Editor]
            .ShouldBe(OptionDesignMetadataAttributeValues.Expression);
        attributes[OptionDesignMetadataAttributeNames.Syntax]
            .ShouldBe(OptionDesignMetadataAttributeValues.Expression);
        attributes[OptionDesignMetadataAttributeNames.RelatedResource]
            .ShouldBe("engine");

        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.option-hints"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("expression"),
                    Kind = OptionValueKind.Expression,
                    Attributes = attributes.ToDictionary(
                        pair => new ComponentAttributeName(pair.Key),
                        pair => new ComponentAttributeValue(pair.Value))
                }
            ]
        };

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
    }

    [Fact]
    public void Option_metadata_attribute_helper_creates_typed_attribute_map()
    {
        var attributes = OptionDesignMetadataAttributes.CreateMap(
            section: "Runtime",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Number);

        attributes[new ComponentAttributeName(OptionDesignMetadataAttributeNames.Section)]
            .ShouldBe(new ComponentAttributeValue("Runtime"));
        attributes[new ComponentAttributeName(OptionDesignMetadataAttributeNames.Importance)]
            .ShouldBe(new ComponentAttributeValue(OptionDesignMetadataAttributeValues.Advanced));
        attributes[new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor)]
            .ShouldBe(new ComponentAttributeValue(OptionDesignMetadataAttributeValues.Number));
    }

    [Theory]
    [InlineData("section")]
    [InlineData("importance")]
    [InlineData("editor")]
    [InlineData("syntax")]
    [InlineData("relatedResource")]
    public void Option_metadata_attribute_helper_rejects_empty_optional_values(string argumentName)
    {
        Action act = argumentName switch
        {
            "section" => () => OptionDesignMetadataAttributes.Create(section: " "),
            "importance" => () => OptionDesignMetadataAttributes.Create(importance: " "),
            "editor" => () => OptionDesignMetadataAttributes.Create(editor: " "),
            "syntax" => () => OptionDesignMetadataAttributes.Create(syntax: " "),
            "relatedResource" => () => OptionDesignMetadataAttributes.Create(relatedResource: " "),
            _ => throw new ArgumentOutOfRangeException(nameof(argumentName))
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Option metadata attribute");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Resource_metadata_attribute_helper_rejects_empty_picker_kind(string pickerKind)
    {
        var act = () => ResourceDesignMetadataAttributes.CreateHostOwned(pickerKind);

        act.ShouldThrow<ArgumentException>()
            .ParamName.ShouldBe("pickerKind");
    }

    [Theory]
    [InlineData("keyPattern")]
    [InlineData("option")]
    [InlineData("requiredWhenAnyOption")]
    public void Resource_metadata_attribute_helper_rejects_empty_optional_values(string argumentName)
    {
        Action act = argumentName switch
        {
            "keyPattern" => () => ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                keyPattern: " "),
            "option" => () => ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                option: " "),
            "requiredWhenAnyOption" => () => ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                requiredWhenAnyOption: " "),
            _ => throw new ArgumentOutOfRangeException(nameof(argumentName))
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Resource metadata attribute");
    }

    [Fact]
    public void Add_registers_and_finds_metadata_by_component_type()
    {
        var metadata = CreateMetadata();

        var catalog = new ComponentDesignMetadataCatalog([metadata]);

        catalog.TryGet(new ComponentType("sample.transform"), out var found).ShouldBeTrue();
        found.ShouldNotBeSameAs(metadata);
        found.Options[0].Kind.ShouldBe(OptionValueKind.Expression);
        found.Ports.Select(port => port.Name.Value).ShouldBe(["Input", "Output", "Events"]);
    }

    [Fact]
    public void Add_snapshots_registered_metadata()
    {
        var metadataAttributes = AttributeMap(("shape", "transform"));
        var optionAttributes = AttributeMap(("scope", "editable"));
        var choiceAttributes = AttributeMap(("kind", "mode"));
        var resourceAttributes = AttributeMap(("resource", "host-owned"));
        var portAttributes = AttributeMap(("side", "input"));
        var choices = new List<OptionChoiceMetadata>
        {
            new()
            {
                Value = new ComponentOptionChoiceValue("strict"),
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

        var catalog = new ComponentDesignMetadataCatalog([metadata]);
        options.Clear();
        choices.Clear();
        resources.Clear();
        ports.Clear();
        metadataAttributes[Attribute("shape")] = new ComponentAttributeValue("changed");
        optionAttributes[Attribute("scope")] = new ComponentAttributeValue("changed");
        choiceAttributes[Attribute("kind")] = new ComponentAttributeValue("changed");
        resourceAttributes[Attribute("resource")] = new ComponentAttributeValue("changed");
        portAttributes[Attribute("side")] = new ComponentAttributeValue("changed");

        catalog.TryGet(metadata.Type, out var found).ShouldBeTrue();

        found.Options.Single(option => option.Name.Value == "mode")
            .Attributes[Attribute("scope")].Value.ShouldBe("editable");
        found.Options[0].Choices.ShouldHaveSingleItem().Attributes[Attribute("kind")].Value.ShouldBe("mode");
        found.Resources.Single(resource => resource.Name.Value == "engine")
            .Attributes[Attribute("resource")].Value.ShouldBe("host-owned");
        found.Ports.Single(port => port.Name.Value == "Input")
            .Attributes[Attribute("side")].Value.ShouldBe("input");
        found.Attributes[Attribute("shape")].Value.ShouldBe("transform");
    }

    [Fact]
    public void Constructor_rejects_duplicate_component_type()
    {
        var act = () => new ComponentDesignMetadataCatalog(
        [
            CreateMetadata(),
            CreateMetadata()
        ]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain("already registered");
    }

    [Fact]
    public void Constructor_treats_null_source_as_empty_and_rejects_null_items()
    {
        var empty = new ComponentDesignMetadataCatalog(null);

        empty.All.ShouldBeEmpty();
        empty.TryGet(new ComponentType("sample.missing"), out var missing).ShouldBeFalse();
        missing.ShouldBeNull();

        var exception = Should.Throw<ArgumentException>(() =>
            new ComponentDesignMetadataCatalog([CreateMetadata("sample.valid"), null!]));
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("cannot contain null values");
    }

    [Fact]
    public void Constructor_loads_metadata_in_source_order()
    {
        var first = CreateMetadata("sample.one");
        var second = CreateMetadata("sample.two");

        var catalog = new ComponentDesignMetadataCatalog([first, second]);

        catalog.All.Count.ShouldBe(2);
        catalog.All.Select(static metadata => metadata.Type.Value)
            .ShouldBe(["sample.one", "sample.two"]);
        catalog.TryGet(first.Type, out _).ShouldBeTrue();
        catalog.TryGet(second.Type, out _).ShouldBeTrue();
    }

    [Fact]
    public void Registration_finalization_preserves_canonical_and_structural_metadata()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().AddComponent("sample.finalized", component =>
        {
            component.UseFactory(static _ => throw new NotSupportedException());
            component.UseProcessing(CompositionProcessingCapabilities.ParallelPreservingOrder);
            component.WithDisplay(displayName: "Finalized component");
            component.AddInput<List<string>>(
                "Input",
                displayName: "Input",
                order: 4,
                isPrimary: true,
                linkCardinality: ComponentPortLinkCardinality.Single);
            component.SetPortAttribute("Input", PortDirection.Input, "port-scope", "original");
            component.AddOutput<Dictionary<string, int>>(
                "Output",
                displayName: "Output",
                order: 2);
            component.AddOption<string>("name", OptionValueKind.Text);
            component.AddOption<int>("BoundedCapacity", OptionValueKind.Number);
            component.AddOption<int>("MaxDegreeOfParallelism", OptionValueKind.Number);
            component.AddOption<bool>("EnsureOrdered", OptionValueKind.Boolean);
            component.AddOption<SampleMode>(
                "mode",
                OptionValueKind.Enum,
                displayName: "Mode",
                isRequired: true);
            component.AddOptionChoice("mode", "relaxed", "Relaxed");
            component.SetOptionAttribute("mode", "option-scope", "original");
            component.AddResource<TimeProvider>(
                "clock",
                displayName: "Clock",
                order: 7,
                isRequired: true);
            component.SetResourceAttribute("clock", "resource-scope", "original");
            component.AddAttribute("component-scope", "original");
        });

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentCatalog>()
            .Descriptors.ShouldHaveSingleItem();
        var catalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();
        catalog.TryGet(new ComponentType("sample.finalized"), out var found).ShouldBeTrue();
        found.ShouldNotBeNull();
        found.ProcessingCapabilities.ShouldBe(
            CompositionProcessingCapabilities.ParallelPreservingOrder);
        found.Options.Single(option => option.Name.Value == "mode")
            .IsRequired.ShouldBeTrue();
        found.Options.Select(static option => option.Name.Value)
            .ShouldBe(["BoundedCapacity", "mode", "processing"]);
        descriptor.Options.Keys.ShouldContain("name");
        descriptor.Options.Keys.ShouldContain("BoundedCapacity");
        descriptor.Options.Keys.ShouldContain("MaxDegreeOfParallelism");
        descriptor.Options.Keys.ShouldContain("EnsureOrdered");
        AttributeValue(found.Attributes, "omittedOptions")
            .ShouldBe("name,MaxDegreeOfParallelism,EnsureOrdered");
        var mode = found.Options.Single(option => option.Name.Value == "mode");
        AttributeValue(mode.Attributes, "option-scope").ShouldBe("original");

        var clock = found.Resources.Single(resource => resource.Name.Value == "clock");
        clock.IsRequired.ShouldBeTrue();
        clock.ValueType?.Value.ShouldBe(nameof(TimeProvider));
        clock.Order.ShouldBe(7);
        AttributeValue(clock.Attributes, "resource-scope").ShouldBe("original");
        var processingResource = found.Resources.Single(resource => resource.Name.Value == "processing");
        processingResource.IsRequired.ShouldBeFalse();
        processingResource.ValueType?.Value.ShouldBe("CompositionProcessingProfile");
        processingResource.Order.ShouldBe(int.MaxValue);

        var processingOption = found.Options.Single(option => option.Name.Value == "processing");
        processingOption.IsRequired.ShouldBeFalse();
        processingOption.Kind.ShouldBe(OptionValueKind.Text);

        var input = found.Ports.Single(port => port.Name.Value == "Input");
        input.MessageType.ShouldBe(typeof(List<string>));
        input.ValueType?.Value.ShouldBe("List<String>");
        input.Kind.ShouldBe(ComponentPortKind.Message);
        input.LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        input.Order.ShouldBe(4);
        input.IsPrimary.ShouldBeTrue();
        AttributeValue(input.Attributes, "port-scope").ShouldBe("original");

        var output = found.Ports.Single(port => port.Name.Value == "Output");
        output.MessageType.ShouldBe(typeof(Dictionary<string, int>));
        output.ValueType?.Value.ShouldBe("Dictionary<String,Int32>");
        output.Kind.ShouldBe(ComponentPortKind.Message);
        output.LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Multiple);
        output.Order.ShouldBe(2);

        var events = found.Ports.Single(port => port.Name.Value == ComponentEvents.PortName);
        events.Direction.ShouldBe(PortDirection.Output);
        events.MessageType.ShouldBe(typeof(ComponentEvent));
        events.ValueType?.Value.ShouldBe(nameof(ComponentEvent));
        events.Group?.Value.ShouldBe("Diagnostics");
        events.Order.ShouldBe(int.MaxValue);
        AttributeValue(found.Attributes, "component-scope").ShouldBe("original");
        ComponentDesignMetadataValidator.Validate(found).ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_snapshots_nested_metadata_and_custom_attributes()
    {
        var choiceAttributes = AttributeMap(("choice", "original"));
        var choices = new List<OptionChoiceMetadata>
        {
            new()
            {
                Value = new ComponentOptionChoiceValue("strict"),
                DisplayName = new ComponentMetadataText("Strict"),
                Attributes = choiceAttributes
            },
            new()
            {
                Value = new ComponentOptionChoiceValue("relaxed"),
                DisplayName = new ComponentMetadataText("Relaxed")
            }
        };
        var optionAttributes = AttributeMap(("option", "original"));
        var resourceAttributes = AttributeMap(("resource", "original"));
        var portAttributes = AttributeMap(("port", "original"));
        var componentAttributes = AttributeMap(("component", "original"));
        var source = CreateMetadata("sample.declaration.snapshot");
        var options = new List<OptionDesignMetadata>
        {
            source.Options[0],
            source.Options[1] with
            {
                Choices = choices,
                Attributes = optionAttributes
            }
        };
        var resources = source.Resources
            .Select((resource, index) => index == 0
                ? resource with { Attributes = resourceAttributes }
                : resource)
            .ToList();
        var ports = source.Ports
            .Select((port, index) => index == 0
                ? port with { Attributes = portAttributes }
                : port)
            .ToList();
        var metadata = source with
        {
            Options = options,
            Resources = resources,
            Ports = ports,
            Attributes = componentAttributes
        };

        var catalog = new ComponentDesignMetadataCatalog([metadata]);

        options.Clear();
        resources.Clear();
        ports.Clear();
        choices.Clear();
        optionAttributes.Clear();
        choiceAttributes.Clear();
        resourceAttributes.Clear();
        portAttributes.Clear();
        componentAttributes.Clear();

        catalog.TryGet(metadata.Type, out var found).ShouldBeTrue();
        found.ShouldNotBeSameAs(metadata);
        found.Options.Single(option => option.Name.Value == "mode")
            .Attributes[Attribute("option")].Value.ShouldBe("original");
        found.Options.Single(option => option.Name.Value == "mode")
            .Choices.Single(choice => choice.Value.Value == "strict")
            .Attributes[Attribute("choice")].Value.ShouldBe("original");
        found.Resources.Single(resource => resource.Name.Value == "engine")
            .Attributes[Attribute("resource")].Value.ShouldBe("original");
        found.Ports.Single(port => port.Name.Value == "Input")
            .Attributes[Attribute("port")].Value.ShouldBe("original");
        found.Attributes[Attribute("component")].Value.ShouldBe("original");
    }

    [Fact]
    public void Constructor_preserves_order_snapshot_isolation_and_cached_read_only_all()
    {
        var firstOptions = CreateMetadata("sample.first").Options.ToList();
        var first = CreateMetadata("sample.first") with { Options = firstOptions };
        var second = CreateMetadata("sample.second");
        var source = new List<ComponentDesignMetadata> { first, second };

        var catalog = new ComponentDesignMetadataCatalog(source);
        var all = catalog.All;
        source.Clear();
        firstOptions.Clear();

        all.ShouldBeSameAs(catalog.All);
        all.Select(static metadata => metadata.Type.Value)
            .ShouldBe(["sample.first", "sample.second"]);
        catalog.TryGet(first.Type, out var found).ShouldBeTrue();
        found.ShouldNotBeSameAs(first);
        found!.Options.ShouldContain(option => option.Name.Value == "expression");
        found.Options.ShouldContain(option => option.Name.Value == "mode");
        var mutable = all.ShouldBeAssignableTo<IList<ComponentDesignMetadata>>();
        Should.Throw<NotSupportedException>(() => mutable!.Add(CreateMetadata("sample.late")));
        catalog.TryGet(new ComponentType("sample.late"), out _).ShouldBeFalse();
    }

    [Fact]
    public void Catalog_rejects_invalid_reserved_component_events_presentation()
    {
        var metadata = CreateMetadata("sample.invalid.events") with
        {
            Ports =
            [
                .. CreateMetadata().Ports,
                new PortDesignMetadata
                {
                    Name = new ComponentPortName(ComponentEvents.PortName),
                    Direction = PortDirection.Input,
                    Order = int.MaxValue,
                    ValueType = new ComponentValueTypeHint(nameof(ComponentEvent))
                }
            ]
        };

        var act = () => new ComponentDesignMetadataCatalog([metadata]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain("reserved for traced component events");
    }

    [Fact]
    public void Flat_registration_automatically_registers_catalog()
    {
        var services = new ServiceCollection();
        RegisterSampleService(services);

        using var provider = services.BuildServiceProvider();

        var declaration = provider
            .GetServices<ComponentDesignDeclaration>()
            .ShouldHaveSingleItem();
        declaration.Descriptor.Type.ShouldBe("sample.service");
        declaration.Metadata.Type.ShouldBe(new ComponentType("sample.service"));

        var catalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();
        catalog.TryGet(new ComponentType("sample.service"), out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull();
    }

    [Fact]
    public void Flat_registration_and_automatic_designer_catalog_are_idempotent()
    {
        var services = new ServiceCollection();
        RegisterSampleService(services);
        RegisterSampleService(services);

        using var provider = services.BuildServiceProvider();

        provider.GetServices<ComponentDesignDeclaration>()
            .ShouldHaveSingleItem();
        provider.GetServices<ComponentDesignMetadataCatalog>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void Validator_reports_invalid_metadata_shape()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.invalid"),
            DisplayName = default(ComponentMetadataText),
            Category = default(ComponentCategory),
            IconKey = default(ComponentIconKey),
            PreferredNodeName = default(ComponentPreferredNodeName),
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
                        new OptionChoiceMetadata { Value = default },
                        new OptionChoiceMetadata { Value = new ComponentOptionChoiceValue("fast") },
                        new OptionChoiceMetadata { Value = new ComponentOptionChoiceValue("fast") }
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
                    Direction = PortDirection.Input,
                    Group = default(ComponentPortGroup)
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
        errors.Select(error => error.Path).ShouldContain(nameof(ComponentDesignMetadata.PreferredNodeName));
        errors.Select(error => error.Path).ShouldContain(nameof(ComponentDesignMetadata.SuggestedEditorWidth));
        errors.ShouldContain(error => error.Path == $"{nameof(ComponentDesignMetadata.Ports)}[1].{nameof(PortDesignMetadata.Group)}");
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
                            Value = new ComponentOptionChoiceValue("strict"),
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
                        new OptionChoiceMetadata { Value = new ComponentOptionChoiceValue("value") }
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
                        new OptionChoiceMetadata { Value = new ComponentOptionChoiceValue("strict") }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("missingMode"),
                    Kind = OptionValueKind.Enum,
                    DefaultValue = "missing",
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = new ComponentOptionChoiceValue("strict") }
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
                        new OptionChoiceMetadata { Value = new ComponentOptionChoiceValue("strict") }
                    ]
                },
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("enumMode"),
                    Kind = OptionValueKind.Enum,
                    DefaultValue = SampleMode.Relaxed,
                    Choices =
                    [
                        new OptionChoiceMetadata { Value = new ComponentOptionChoiceValue(nameof(SampleMode.Relaxed)) }
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
                    DisplayName = default(ComponentMetadataText),
                    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
                    {
                        [default] = new ComponentAttributeValue("resource")
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
    public void ComponentPreferredNodeName_validates_value_and_preserves_identity()
    {
        var first = new ComponentPreferredNodeName("transform");
        var second = new ComponentPreferredNodeName("transform");

        first.ShouldBe(second);
        first.Value.ShouldBe("transform");
        first.ToString().ShouldBe("transform");
        new ComponentPreferredNodeName("source").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("node.name")]
    public void ComponentPreferredNodeName_rejects_invalid_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentPreferredNodeName(value);
        };

        act.ShouldThrow<ArgumentException>();
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
    public void ComponentOptionChoiceValue_validates_value_and_preserves_identity()
    {
        var first = new ComponentOptionChoiceValue("strict");
        var second = new ComponentOptionChoiceValue("strict");

        first.ShouldBe(second);
        first.Value.ShouldBe("strict");
        first.ToString().ShouldBe("strict");
        new ComponentOptionChoiceValue("relaxed").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentOptionChoiceValue_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentOptionChoiceValue(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component option choice value cannot be empty");
    }

    [Fact]
    public void ComponentAttributeName_validates_value_and_preserves_identity()
    {
        var first = new ComponentAttributeName("shape");
        var second = new ComponentAttributeName("shape");

        first.ShouldBe(second);
        first.Value.ShouldBe("shape");
        first.ToString().ShouldBe("shape");
        new ComponentAttributeName("domain").ShouldNotBe(first);
    }

    [Fact]
    public void ComponentAttributeValue_validates_value_and_preserves_identity()
    {
        var first = new ComponentAttributeValue("transform");
        var second = new ComponentAttributeValue("transform");

        first.ShouldBe(second);
        first.Value.ShouldBe("transform");
        first.ToString().ShouldBe("transform");
        new ComponentAttributeValue("source").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentAttributeValue_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentAttributeValue(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component attribute value cannot be empty");
    }

    [Fact]
    public void ComponentMetadataText_validates_value_and_preserves_identity()
    {
        var first = new ComponentMetadataText("Sample Transform");
        var second = new ComponentMetadataText("Sample Transform");

        first.ShouldBe(second);
        first.Value.ShouldBe("Sample Transform");
        first.ToString().ShouldBe("Sample Transform");
        new ComponentMetadataText("Other Transform").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentMetadataText_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentMetadataText(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component metadata text cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentAttributeName_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentAttributeName(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component attribute name cannot be empty");
    }

    [Fact]
    public void ComponentValueTypeHint_validates_value_and_preserves_identity()
    {
        var first = new ComponentValueTypeHint("SampleInput");
        var second = new ComponentValueTypeHint("SampleInput");

        first.ShouldBe(second);
        first.Value.ShouldBe("SampleInput");
        first.ToString().ShouldBe("SampleInput");
        new ComponentValueTypeHint("SampleOutput").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentValueTypeHint_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentValueTypeHint(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component value type hint cannot be empty");
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
        resources[0].DisplayName?.Value.ShouldBe("Engine");
        resources[0].ValueType?.Value.ShouldBe("IExpressionEngine");
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
    public void ComponentPortGroup_validates_value_and_preserves_identity()
    {
        var first = new ComponentPortGroup("Messages");
        var second = new ComponentPortGroup("Messages");

        first.ShouldBe(second);
        first.Value.ShouldBe("Messages");
        first.ToString().ShouldBe("Messages");
        new ComponentPortGroup("Errors").ShouldNotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ComponentPortGroup_rejects_empty_values(string value)
    {
        var act = () =>
        {
            _ = new ComponentPortGroup(value);
        };

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Component port group cannot be empty");
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
        ports[0].Group.ShouldBe(new ComponentPortGroup("Messages"));
        ports[0].ValueType?.Value.ShouldBe("SampleInput");
        ports[0].IsPrimary.ShouldBeTrue();
        ports[1].Name.ShouldBe(new ComponentPortName("Output"));
        ports[1].Direction.ShouldBe(PortDirection.Output);
    }

    private static ComponentDesignMetadata CreateMetadata(string type = "sample.transform") => new()
    {
        Type = new ComponentType(type),
        DisplayName = new ComponentMetadataText("Sample Transform"),
        Category = new ComponentCategory("Samples"),
        Summary = new ComponentMetadataText("Transforms sample values."),
        IconKey = new ComponentIconKey("transform"),
        PreferredNodeName = new ComponentPreferredNodeName("transform"),
        SuggestedEditorWidth = 420,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = new ComponentOptionName("expression"),
                Kind = OptionValueKind.Expression,
                DisplayName = new ComponentMetadataText("Expression"),
                HelperText = new ComponentMetadataText("Expression evaluated for each input."),
                IsRequired = true
            },
            new OptionDesignMetadata
            {
                Name = new ComponentOptionName("mode"),
                Kind = OptionValueKind.Enum,
                DefaultValue = "strict",
                Choices =
                [
                    new OptionChoiceMetadata
                    {
                        Value = new ComponentOptionChoiceValue("strict"),
                        DisplayName = new ComponentMetadataText("Strict")
                    },
                    new OptionChoiceMetadata
                    {
                        Value = new ComponentOptionChoiceValue("relaxed"),
                        DisplayName = new ComponentMetadataText("Relaxed")
                    }
                ]
            }
        ],
        Resources =
        [
            new ResourceDesignMetadata
            {
                Name = new ComponentResourceName("engine"),
                DisplayName = new ComponentMetadataText("Engine"),
                Order = 0,
                Summary = new ComponentMetadataText("Expression engine resource."),
                ValueType = new ComponentValueTypeHint("IExpressionEngine"),
                IsRequired = true
            },
            new ResourceDesignMetadata
            {
                Name = new ComponentResourceName("clock"),
                DisplayName = new ComponentMetadataText("Clock"),
                Order = 1,
                Summary = new ComponentMetadataText("Optional clock resource."),
                ValueType = new ComponentValueTypeHint(nameof(TimeProvider))
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName("Input"),
                Direction = PortDirection.Input,
                DisplayName = new ComponentMetadataText("Input"),
                Group = new ComponentPortGroup("Messages"),
                Order = 0,
                Summary = new ComponentMetadataText("Input message."),
                ValueType = new ComponentValueTypeHint("SampleInput"),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName("Output"),
                Direction = PortDirection.Output,
                DisplayName = new ComponentMetadataText("Output"),
                Group = new ComponentPortGroup("Messages"),
                Order = 1,
                Summary = new ComponentMetadataText("Mapped message."),
                ValueType = new ComponentValueTypeHint("SampleOutput"),
                IsPrimary = true
            }
        ],
        Attributes = AttributeMap(("shape", "transform"))
    };

    private static ComponentDescriptor CreateDescriptor(
        string type,
        CompositionProcessingCapabilities processingCapabilities =
            CompositionProcessingCapabilities.Sequential)
        => new(
            type,
            static _ => throw new NotSupportedException("The metadata test descriptor is not activated."),
            [ComponentPorts.Metadata<string>("Input", ComponentPortLinkCardinality.Single)],
            [ComponentPorts.Metadata<int>("Output")],
            processingCapabilities,
            options:
            [
                ComponentOptions.Metadata<string>("expression", isRequired: true),
                ComponentOptions.Metadata<SampleMode>("mode")
            ],
            resources:
            [
                ComponentResources.Metadata<object>(
                    "engine",
                    isRequired: true,
                    valueTypeHint: "IExpressionEngine"),
                ComponentResources.Metadata<TimeProvider>("clock")
            ]);

    private static FluxFlowRegistrationBuilder RegisterSampleService(IServiceCollection services)
        => services.AddFluxFlowComponents().AddComponent("sample.service", component =>
        {
            component.UseFactory(static _ =>
                throw new NotSupportedException("The metadata test component is not activated."));
            component.WithDisplay(displayName: "Sample Service");
        });

    private static ComponentAttributeName Attribute(string name) => new(name);

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[Attribute(name)].Value;

    private static Dictionary<ComponentAttributeName, ComponentAttributeValue> AttributeMap(
        params (string Name, string Value)[] attributes)
        => attributes.ToDictionary(
            attribute => Attribute(attribute.Name),
            attribute => new ComponentAttributeValue(attribute.Value));

    private enum SampleMode
    {
        Relaxed
    }
}
