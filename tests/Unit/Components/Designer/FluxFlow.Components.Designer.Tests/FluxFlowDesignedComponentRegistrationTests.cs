using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class FluxFlowDesignedComponentRegistrationTests
{
    [Fact]
    public void Design_decoration_validates_enum_presentation_after_decoration()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var activations = 0;
        builder.Advanced.AddDynamicComponent("test.enum", component =>
        {
            component.UseFactory(_ => { activations++; return new RegistrationNode(); })
                .HasInput("Input", SelectInput).HasOutput("Output", SelectOutput).HasEvents("Events", SelectEvents);
            component.AddOption<DayOfWeek>("required", isRequired: true);
            component.AddOption<DayOfWeek?>("optional");
        });
        var descriptor = ReadDescriptors(services).ShouldHaveSingleItem();
        var callbacks = 0;
        builder.AddDesignMetadata("test.enum", metadata =>
        {
            callbacks++;
            return metadata with
            {
                Options = metadata.Options.Select(option => option.Kind == OptionValueKind.Enum
                    ? option with
                    {
                        DefaultValue = "Monday",
                        Choices = [new OptionChoiceMetadata { Value = new("Monday"), DisplayName = new("Start of week") }]
                    } : option).ToArray()
            };
        });
        using var provider = services.BuildServiceProvider();
        var presented = provider.GetRequiredService<ComponentDesignMetadataCatalog>().All.ShouldHaveSingleItem();
        var enums = presented.Options.Where(option => option.Kind == OptionValueKind.Enum).ToArray();
        enums.Select(option => (option.Name.Value, option.IsRequired)).ShouldBe([("required", true), ("optional", false)]);
        foreach (var option in enums)
        {
            option.DefaultValue.ShouldBe("Monday");
            option.Choices.ShouldHaveSingleItem().DisplayName?.Value.ShouldBe("Start of week");
        }
        var declaration = ReadDeclarations(services).ShouldHaveSingleItem();
        Should.Throw<InvalidOperationException>(() => builder.AddDesignMetadata("test.enum", metadata => metadata))
            .Message.ShouldContain("Enum options must define at least one choice");
        ReadDeclarations(services).ShouldHaveSingleItem().ShouldBeSameAs(declaration);
        ReadDescriptors(services).ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
        callbacks.ShouldBe(1);
        activations.ShouldBe(0);
    }

    [Fact]
    public void Design_decoration_reuses_registered_structure_without_activation()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var activations = 0;
        builder.Advanced.AddDynamicComponent("test.component", component =>
        {
            component.UseFactory(_ => { activations++; return new RegistrationNode(); })
                .HasInput("Input", SelectInput)
                .HasOutput("Output", SelectOutput, ComponentPortLinkCardinality.Single)
                .HasEvents("Events", SelectEvents);
            component.AddOption<bool>("enabled", isRequired: true);
            component.AddResource<TimeProvider>("clock", isRequired: true);
        });
        var descriptor = ReadDescriptors(services).ShouldHaveSingleItem();
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        { [new("color")] = new("blue") };

        builder.AddDesignMetadata(" test.component ", metadata => metadata with
        {
            DisplayName = new ComponentMetadataText("Decorated"),
            Attributes = attributes,
            Ports = metadata.Ports.Select(port => port with { Order = port.Order + 7 }).ToArray()
        }).ShouldBeSameAs(builder);
        attributes[new("color")] = new("red");

        activations.ShouldBe(0);
        ReadDescriptors(services).ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>().All.ShouldHaveSingleItem();
        provider.GetRequiredService<ComponentDesignDeclaration>().Descriptor.ShouldBeSameAs(descriptor);
        metadata.DisplayName?.Value.ShouldBe("Decorated");
        metadata.Attributes[new("color")].Value.ShouldBe("blue");
        metadata.Ports.Select(port => port.Name.Value).ShouldBe(["Input", "Output", "Events"]);
        metadata.Ports[0].MessageType.ShouldBe(typeof(global::FluxFlow.Data.FlowValue));
        metadata.Ports[0].Order.ShouldBe(7);
        metadata.Ports[1].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        metadata.Options.Single(option => option.Name.Value == "enabled").IsRequired.ShouldBeTrue();
        metadata.Resources.Single(resource => resource.Name.Value == "clock").IsRequired.ShouldBeTrue();
        activations.ShouldBe(0);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("processing")]
    [InlineData("port")]
    [InlineData("port-cardinality")]
    [InlineData("option")]
    [InlineData("resource")]
    public void Design_decoration_rejects_structural_changes(string change)
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        builder.Advanced.AddDynamicComponent("test.component", ConfigureRuntime);
        var descriptor = ReadDescriptors(services).ShouldHaveSingleItem();
        Func<ComponentDesignMetadata, ComponentDesignMetadata> modify = change switch
        {
            "type" => metadata => metadata with { Type = new ComponentType("other") },
            "processing" => metadata => metadata with { ProcessingCapabilities = CompositionProcessingCapabilities.Sequential },
            "port" => metadata => metadata with { Ports = metadata.Ports.Skip(1).ToArray() },
            "port-cardinality" => metadata => metadata with { Ports = metadata.Ports.Select(port => port with { LinkCardinality = ComponentPortLinkCardinality.Multiple }).ToArray() },
            "option" => metadata => metadata with { Options = metadata.Options.Select(option => option with { IsRequired = false }).ToArray() },
            _ => metadata => metadata with { Resources = metadata.Resources.Select(resource => resource with { IsRequired = false }).ToArray() }
        };

        Should.Throw<InvalidOperationException>(() => builder.AddDesignMetadata("test.component", modify))
            .Message.ShouldContain("structural facts");
        ReadDescriptors(services).ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
        ReadDeclarations(services).ShouldBeEmpty();
        services.ShouldNotContain(service => service.ServiceType == typeof(ComponentDesignMetadataCatalog));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Design_decoration_preserves_input_shapes_and_signal_roles(bool changeSignal)
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var shape = global::FluxFlow.Data.FlowValueShape.String("Text");
        var activations = 0;
        builder.Advanced.AddDynamicComponent("test.shaped", component =>
            component.UseInstanceFactory(_ => { activations++; throw new InvalidOperationException("Must not activate"); })
                .HasInput("Input", shape).HasSignalInput("Stop"));
        builder.AddDesignMetadata("test.shaped", metadata => metadata);
        var original = ReadDeclarations(services).ShouldHaveSingleItem();
        original.Metadata.Ports[0].InputShape.ShouldBeSameAs(shape);
        original.Metadata.Ports[0].MessageType.ShouldBe(typeof(global::FluxFlow.Data.FlowValue));
        original.Metadata.Ports[1].Kind.ShouldBe(ComponentPortKind.Signal);

        Should.Throw<InvalidOperationException>(() => builder.AddDesignMetadata("test.shaped", metadata => metadata with
        {
            Ports = metadata.Ports.Select(port => port.Name.Value == (changeSignal ? "Stop" : "Input")
                ? port with { Kind = ComponentPortKind.Message, InputShape = null } : port).ToArray()
        })).Message.ShouldContain("structural facts");
        ReadDeclarations(services).ShouldHaveSingleItem().ShouldBeSameAs(original);
        activations.ShouldBe(0);
    }

    [Fact]
    public void Design_decoration_requires_a_registered_component_and_rejects_conflicts()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var callbacks = 0;
        Should.Throw<InvalidOperationException>(() => builder.AddDesignMetadata("missing", metadata =>
        { callbacks++; return metadata; })).Message.ShouldContain("must be registered");
        callbacks.ShouldBe(0);
        builder.Advanced.AddDynamicComponent("test.component", ConfigureRuntime);
        builder.AddDesignMetadata("test.component", metadata => metadata with { DisplayName = new("First") });
        var declaration = ReadDeclarations(services).ShouldHaveSingleItem();
        builder.AddDesignMetadata("test.component", metadata => metadata with { DisplayName = new("First") });
        ReadDeclarations(services).ShouldHaveSingleItem().ShouldBeSameAs(declaration);
        Should.Throw<InvalidOperationException>(() => builder.AddDesignMetadata("test.component",
            metadata => metadata with { DisplayName = new("Second") })).Message.ShouldContain("conflicting design registration");
        Should.Throw<InvalidOperationException>(() => builder.AddDesignMetadata("test.component", _ => null!))
            .Message.ShouldContain("cannot be null");
        ReadDeclarations(services).ShouldHaveSingleItem().ShouldBeSameAs(declaration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentDesignMetadataCatalog>().All.ShouldHaveSingleItem().DisplayName?.Value.ShouldBe("First");
    }

    [Fact]
    public void AddComponent_automatically_registers_one_immutable_design_catalog()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents()
            .AddComponent("test.component", ConfigureDesigned);

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var declaration = provider.GetRequiredService<ComponentDesignDeclaration>();
        var runtimeCatalog = provider.GetRequiredService<ComponentCatalog>();
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem();

        declaration.Descriptor.ShouldBeSameAs(descriptor);
        declaration.Metadata.Type.Value.ShouldBe(descriptor.Type);
        runtimeCatalog.Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
        metadata.Type.Value.ShouldBe("test.component");
        metadata.ProcessingCapabilities.ShouldBe(
            CompositionProcessingCapabilities.ParallelRelaxedOrder);
        metadata.Options.ShouldContain(option =>
            option.Name.Value == "enabled" &&
            option.Kind == OptionValueKind.Boolean &&
            option.IsRequired);
        metadata.Resources.ShouldContain(resource =>
            resource.Name.Value == "clock" &&
            resource.ValueType.HasValue &&
            resource.ValueType.Value.Value == nameof(TimeProvider) &&
            resource.IsRequired);
        metadata.Ports.ShouldContain(port =>
            port.Name.Value == "Input" &&
            port.Direction == PortDirection.Input &&
            port.MessageType == typeof(global::FluxFlow.Data.FlowValue));
        metadata.Ports.ShouldContain(port =>
            port.Name.Value == "Output" &&
            port.Direction == PortDirection.Output &&
            port.MessageType == typeof(global::FluxFlow.Data.FlowValue));
    }

    [Fact]
    public void AddComponent_returns_same_flat_builder_and_invokes_configuration_once()
    {
        var builder = new ServiceCollection().AddFluxFlowComponents();
        var invocationCount = 0;
        ComponentRegistrationBuilder? configured = null;

        var returned = builder.AddComponent("test.component", component =>
        {
            invocationCount++;
            configured = component;
            ConfigureDesigned(component);
        });

        returned.ShouldBeSameAs(builder);
        invocationCount.ShouldBe(1);
        configured.ShouldNotBeNull();
    }

    [Fact]
    public void Equivalent_designed_registration_is_idempotent_across_flat_builders()
    {
        var services = new ServiceCollection();

        services.AddFluxFlowComponents()
            .AddComponent("test.component", ConfigureDesigned)
            .AddComponent("test.component", ConfigureDesigned);
        services.AddFluxFlowComponents()
            .AddComponent("test.component", ConfigureDesigned);

        services.Count(registration => registration.ServiceType == typeof(ComponentCatalog))
            .ShouldBe(1);
        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);
        services.Count(registration => registration.ServiceType == typeof(ComponentDesignDeclaration))
            .ShouldBe(1);
        services.Count(registration => registration.ServiceType == typeof(ComponentDesignMetadataCatalog))
            .ShouldBe(1);

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        provider.GetRequiredService<ComponentDesignDeclaration>()
            .Descriptor.ShouldBeSameAs(descriptor);
        provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem().Type.Value.ShouldBe("test.component");
    }

    [Fact]
    public void Conflicting_design_registration_fails_immediately_without_partial_state()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents()
            .AddComponent("test.component", component =>
            {
                ConfigureDesigned(component);
                component.WithDisplay(displayName: "First");
            });
        var originalDescriptor = ReadDescriptors(services).ShouldHaveSingleItem();
        var originalDeclaration = ReadDeclarations(services).ShouldHaveSingleItem();

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddComponent("test.component", component =>
            {
                ConfigureDesigned(component);
                component.WithDisplay(displayName: "Second");
            }));

        exception.Message.ShouldContain("test.component");
        exception.Message.ShouldContain("conflicting design registration");
        ReadDescriptors(services).ShouldHaveSingleItem().ShouldBeSameAs(originalDescriptor);
        ReadDeclarations(services).ShouldHaveSingleItem().ShouldBeSameAs(originalDeclaration);
    }

    [Fact]
    public void Conflicting_runtime_registration_from_designed_path_preserves_original_pair()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents()
            .AddComponent("test.component", ConfigureDesigned);
        var originalDescriptor = ReadDescriptors(services).ShouldHaveSingleItem();
        var originalDeclaration = ReadDeclarations(services).ShouldHaveSingleItem();

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddComponent("test.component", ConfigureDesignedWithConflictingFactory));

        exception.Message.ShouldContain("test.component");
        exception.Message.ShouldContain("conflicting descriptor registration");
        ReadDescriptors(services).ShouldHaveSingleItem().ShouldBeSameAs(originalDescriptor);
        ReadDeclarations(services).ShouldHaveSingleItem().ShouldBeSameAs(originalDeclaration);
        originalDeclaration.Descriptor.ShouldBeSameAs(originalDescriptor);
    }

    [Fact]
    public void Runtime_then_designed_registration_reuses_one_descriptor_owner()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        builder.Advanced.AddDynamicComponent("test.component", ConfigureRuntime);

        builder.AddComponent("test.component", ConfigureDesigned)
            .ShouldBeSameAs(builder);

        ReadDescriptors(services).Length.ShouldBe(1);
        ReadDeclarations(services).Length.ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        provider.GetRequiredService<ComponentDesignDeclaration>()
            .Descriptor.ShouldBeSameAs(descriptor);
        provider.GetRequiredService<ComponentCatalog>()
            .Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
    }

    [Fact]
    public void Designed_then_runtime_registration_reuses_one_descriptor_owner()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents()
            .AddComponent("test.component", ConfigureDesigned);
        var advanced = builder.Advanced;

        advanced.AddDynamicComponent("test.component", ConfigureRuntime)
            .ShouldBeSameAs(advanced);

        ReadDescriptors(services).Length.ShouldBe(1);
        ReadDeclarations(services).Length.ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        provider.GetRequiredService<ComponentDesignDeclaration>()
            .Descriptor.ShouldBeSameAs(descriptor);
        provider.GetRequiredService<ComponentCatalog>()
            .Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
    }

    [Fact]
    public void Advanced_dynamic_component_remains_runtime_only()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var advanced = builder.Advanced;

        var returned = advanced.AddDynamicComponent("test.component", ConfigureRuntime);

        returned.ShouldBeSameAs(advanced);
        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);
        services.Count(registration => registration.ServiceType == typeof(ComponentDesignDeclaration))
            .ShouldBe(0);
        services.Count(registration => registration.ServiceType == typeof(ComponentDesignMetadataCatalog))
            .ShouldBe(0);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>().Components.Keys
            .ShouldBe(["test.component"]);
        provider.GetService<ComponentDesignMetadataCatalog>().ShouldBeNull();
    }

    [Fact]
    public void Configuration_failure_does_not_append_partial_registration_state()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var baseline = services.ToArray();
        var expected = new InvalidOperationException("Distinct configuration failure.");

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddComponent("test.component", _ => throw expected));

        exception.ShouldBeSameAs(expected);
        services.ShouldNotContain(registration =>
            registration.ServiceType == typeof(ComponentDescriptor));
        services.ShouldNotContain(registration =>
            registration.ServiceType == typeof(ComponentDesignDeclaration));
        services.ShouldNotContain(registration =>
            registration.ServiceType == typeof(ComponentDesignMetadataCatalog));
        services.Count.ShouldBe(baseline.Length);
        for (var index = 0; index < baseline.Length; index++)
            services[index].ShouldBeSameAs(baseline[index]);
    }

    [Fact]
    public void Resolved_designer_catalog_is_detached_from_retained_component_builder()
    {
        var services = new ServiceCollection();
        ComponentRegistrationBuilder? retained = null;
        DesignedComponentBindingBuilder<RegistrationNode>? retainedBindings = null;
        services.AddFluxFlowComponents()
            .AddComponent("test.component", component =>
            {
                retained = component;
                retainedBindings = ConfigureMutableDesigned(component);
            });

        retained.ShouldNotBeNull();
        retained.AddOptionChoice("mode", "late");
        retained.SetOptionAttribute("mode", "phase", "late");
        retained.SetResourceAttribute("clock", "phase", "late");
        retained.SetPortAttribute("Input", PortDirection.Input, "phase", "late");
        retained.AddAttribute("late", "value");
        retainedBindings.ShouldNotBeNull()
            .HasOutput("Late", static node => node.Late, displayName: "Late");

        using var provider = services.BuildServiceProvider();
        var declaration = provider.GetRequiredService<ComponentDesignDeclaration>();
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        declaration.Metadata.Options.Single(option => option.Name.Value == "mode")
            .Choices.Select(choice => choice.Value.Value).ShouldBe(["initial"]);
        AttributeValue(
            declaration.Metadata.Options.Single(option => option.Name.Value == "mode").Attributes,
            "phase").ShouldBe("initial");
        AttributeValue(
            declaration.Metadata.Resources.Single(resource => resource.Name.Value == "clock").Attributes,
            "phase").ShouldBe("initial");
        AttributeValue(
            declaration.Metadata.Ports.Single(port => port.Name.Value == "Input").Attributes,
            "phase").ShouldBe("initial");
        declaration.Metadata.Attributes.ContainsKey(new ComponentAttributeName("late"))
            .ShouldBeFalse();
        declaration.Metadata.Ports.ShouldNotContain(port => port.Name.Value == "Late");
        metadata.Options.Single(option => option.Name.Value == "mode")
            .Choices.Select(choice => choice.Value.Value).ShouldBe(["initial"]);
        metadata.Ports.ShouldNotContain(port => port.Name.Value == "Late");
        descriptor.Outputs.ContainsKey("Late").ShouldBeFalse();
    }

    [Fact]
    public void Standalone_flat_designed_usage_compiles_without_Engine()
    {
        IServiceCollection services = new ServiceCollection();
        FluxFlowRegistrationBuilder registration = services.AddFluxFlowComponents();

        FluxFlowRegistrationBuilder returned = registration
            .AddComponent("test.component", component =>
            {
                component.UseFactory(CreateNode);
                component.WithDisplay(displayName: "Test");
            });

        returned.ShouldBeSameAs(registration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>().Components.Keys
            .ShouldBe(["test.component"]);
        provider.GetRequiredService<ComponentDesignMetadataCatalog>().All
            .ShouldHaveSingleItem().Type.Value.ShouldBe("test.component");
    }

    private static void ConfigureRuntime(RuntimeComponentRegistrationBuilder component)
    {
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component
            .UseFactory(CreateNode)
            .HasInput("Input", SelectInput)
            .HasOutput(
                "Output",
                SelectOutput,
                ComponentPortLinkCardinality.Single)
            .HasEvents("Events", SelectEvents);
        component.AddOption<bool>("enabled", isRequired: true);
        component.AddResource<TimeProvider>("clock", isRequired: true);
    }

    [Fact]
    public void Complete_contract_registration_projects_design_metadata_without_activating_factory()
    {
        var activations = 0;
        var contract = DesignedComponentContract.Create(
            " test.component ",
            component =>
            {
                component.UseProcessing(
                    CompositionProcessingCapabilities.ParallelRelaxedOrder);
                component.WithDisplay(
                    displayName: "Designed contract",
                    category: "Testing",
                    summary: "One complete declaration");
                component
                    .UseFactory(_ =>
                    {
                        activations++;
                        return new RegistrationNode();
                    })
                    .HasInput(
                        "Input",
                        SelectInput,
                        displayName: "Input",
                        order: 7)
                    .HasOutput(
                        "Output",
                        SelectOutput,
                        displayName: "Output",
                        order: 11,
                        linkCardinality: ComponentPortLinkCardinality.Single)
                    .HasEvents(
                        "Events",
                        SelectEvents,
                        displayName: "Events",
                        order: 12);
                component.AddOption<bool>(
                    "enabled",
                    OptionValueKind.Boolean,
                    displayName: "Enabled",
                    isRequired: true);
                component.AddResource<TimeProvider>(
                    "clock",
                    "Clock",
                    isRequired: true);
            },
            static component => new InputOutputComponentHandle<string, int>(
                component,
                "Input",
                "Output",
                "Events"));
        activations.ShouldBe(0);
        var application = new ApplicationDefinitionBuilder();
        application.AddWorkflow("Main").AddComponent("Node", contract);
        var definition = application.Build();
        definition.ComponentDescriptors.ShouldHaveSingleItem()
            .ShouldBeSameAs(contract.Descriptor);
        activations.ShouldBe(0);
        var services = new ServiceCollection();
        var registration = services.AddFluxFlowComponents();

        registration.AddDesignedComponent(contract).ShouldBeSameAs(registration);
        activations.ShouldBe(0);
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var declaration = provider.GetRequiredService<ComponentDesignDeclaration>();
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem();

        descriptor.ShouldBeSameAs(contract.Descriptor);
        declaration.Descriptor.ShouldBeSameAs(contract.Descriptor);
        metadata.Type.Value.ShouldBe("test.component");
        metadata.DisplayName?.Value.ShouldBe("Designed contract");
        metadata.Category?.Value.ShouldBe("Testing");
        metadata.Summary?.Value.ShouldBe("One complete declaration");
        metadata.ProcessingCapabilities.ShouldBe(
            CompositionProcessingCapabilities.ParallelRelaxedOrder);
        metadata.Ports.Single(port =>
                port.Name.Value == "Input" && port.Direction == PortDirection.Input)
            .Order.ShouldBe(7);
        metadata.Ports.Single(port =>
                port.Name.Value == "Output" && port.Direction == PortDirection.Output)
            .Order.ShouldBe(11);
        metadata.Ports.Single(port =>
                port.Name.Value == "Events" && port.Direction == PortDirection.Output)
            .Order.ShouldBe(12);
        metadata.Options.Single(option => option.Name.Value == "enabled")
            .IsRequired.ShouldBeTrue();
        metadata.Resources.Single(resource => resource.Name.Value == "clock")
            .IsRequired.ShouldBeTrue();
        provider.GetRequiredService<ComponentCatalog>().Descriptors
            .ShouldHaveSingleItem().ShouldBeSameAs(contract.Descriptor);
        activations.ShouldBe(0);
    }

    private static void ConfigureDesigned(ComponentRegistrationBuilder component)
        => ConfigureDesigned(component, CreateNode);

    private static void ConfigureDesignedWithConflictingFactory(ComponentRegistrationBuilder component)
        => ConfigureDesigned(component, CreateConflictingNode);

    private static void ConfigureDesigned(
        ComponentRegistrationBuilder component,
        Func<ComponentActivationContext, RegistrationNode> factory)
    {
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component.WithDisplay(
            displayName: "Test component",
            category: "Testing",
            summary: "Registration test component");
        component
            .UseFactory(factory)
            .HasInput("Input", SelectInput, displayName: "Input")
            .HasOutput(
                "Output",
                SelectOutput,
                displayName: "Output",
                linkCardinality: ComponentPortLinkCardinality.Single)
            .HasEvents("Events", SelectEvents, displayName: "Events");
        component.AddOption<bool>(
            "enabled",
            OptionValueKind.Boolean,
            displayName: "Enabled",
            isRequired: true);
        component.AddResource<TimeProvider>(
            "clock",
            "Clock",
            isRequired: true);
    }

    private static DesignedComponentBindingBuilder<RegistrationNode> ConfigureMutableDesigned(
        ComponentRegistrationBuilder component)
    {
        component.WithDisplay(displayName: "Mutable test component");
        var bindings = component
            .UseFactory(CreateNode)
            .HasInput("Input", static node => node.Input, displayName: "Input")
            .HasOutput("Output", static node => node.Output, displayName: "Output");
        component.SetPortAttribute("Input", PortDirection.Input, "phase", "initial");
        component.AddOption<string>("mode", OptionValueKind.Enum, displayName: "Mode");
        component.AddOptionChoice("mode", "initial");
        component.SetOptionAttribute("mode", "phase", "initial");
        component.AddResource<TimeProvider>("clock", "Clock");
        component.SetResourceAttribute("clock", "phase", "initial");
        component.AddAttribute("phase", "initial");
        return bindings;
    }

    private static ComponentDescriptor[] ReadDescriptors(IServiceCollection services)
        => services
            .Where(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .Select(registration => registration.ImplementationInstance)
            .OfType<ComponentDescriptor>()
            .ToArray();

    private static ComponentDesignDeclaration[] ReadDeclarations(IServiceCollection services)
        => services
            .Where(registration => registration.ServiceType == typeof(ComponentDesignDeclaration))
            .Select(registration => registration.ImplementationInstance)
            .OfType<ComponentDesignDeclaration>()
            .ToArray();

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static RegistrationNode CreateNode(ComponentActivationContext _) => new();

    private static RegistrationNode CreateConflictingNode(ComponentActivationContext _) => new();

    private static ITargetBlock<FlowMessage> SelectInput(RegistrationNode node) => node.Input;

    private static ISourceBlock<FlowMessage> SelectOutput(RegistrationNode node) => node.Output;

    private static ISourceBlock<FlowMessage> SelectEvents(RegistrationNode node) => node.Events;

    private sealed class RegistrationNode : IFlowNode
    {
        public BufferBlock<FlowMessage> Input { get; } = new();

        public BufferBlock<FlowMessage> Output { get; } = new();

        public BufferBlock<FlowMessage> Late { get; } = new();

        public BufferBlock<FlowMessage> Events { get; } = new();

        public Task Completion { get; } = Task.CompletedTask;

        public void Complete()
        {
            Input.Complete();
            Output.Complete();
            Late.Complete();
            Events.Complete();
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }
}
