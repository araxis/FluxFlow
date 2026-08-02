using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class FluxFlowDesignedComponentRegistrationTests
{
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
            port.MessageType == typeof(string));
        metadata.Ports.ShouldContain(port =>
            port.Name.Value == "Output" &&
            port.Direction == PortDirection.Output &&
            port.MessageType == typeof(int));
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
            builder.AddComponent("test.component", component =>
            {
                ConfigureDesigned(component);
                component.UseFactory(ConflictingFactory);
            }));

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
        var builder = services.AddFluxFlowComponents()
            .AddRuntimeComponent("test.component", ConfigureRuntime);

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

        builder.AddRuntimeComponent("test.component", ConfigureRuntime)
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
    public void AddRuntimeComponent_remains_runtime_only()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();

        var returned = builder.AddRuntimeComponent("test.component", ConfigureRuntime);

        returned.ShouldBeSameAs(builder);
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
        services.AddFluxFlowComponents()
            .AddComponent("test.component", component =>
            {
                retained = component;
                ConfigureMutableDesigned(component);
            });

        retained.ShouldNotBeNull();
        retained.AddOptionChoice("mode", "late");
        retained.SetOptionAttribute("mode", "phase", "late");
        retained.SetResourceAttribute("clock", "phase", "late");
        retained.SetPortAttribute("Input", PortDirection.Input, "phase", "late");
        retained.AddAttribute("late", "value");
        retained.AddOutput<Guid>("Late", "Late");

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
                component.UseFactory(UnusedFactory);
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
        component.UseFactory(UnusedFactory);
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component.AddInput<string>("Input");
        component.AddOutput<int>("Output", ComponentPortLinkCardinality.Single);
        component.AddOption<bool>("enabled", isRequired: true);
        component.AddResource<TimeProvider>("clock", isRequired: true);
    }

    private static void ConfigureDesigned(ComponentRegistrationBuilder component)
    {
        component.UseFactory(UnusedFactory);
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component.WithDisplay(
            displayName: "Test component",
            category: "Testing",
            summary: "Registration test component");
        component.AddInput<string>("Input", "Input");
        component.AddOutput<int>(
            "Output",
            "Output",
            linkCardinality: ComponentPortLinkCardinality.Single);
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

    private static void ConfigureMutableDesigned(ComponentRegistrationBuilder component)
    {
        component.UseFactory(UnusedFactory);
        component.WithDisplay(displayName: "Mutable test component");
        component.AddInput<string>("Input", "Input");
        component.SetPortAttribute("Input", PortDirection.Input, "phase", "initial");
        component.AddOutput<int>("Output", "Output");
        component.AddOption<string>("mode", OptionValueKind.Enum, displayName: "Mode");
        component.AddOptionChoice("mode", "initial");
        component.SetOptionAttribute("mode", "phase", "initial");
        component.AddResource<TimeProvider>("clock", "Clock");
        component.SetResourceAttribute("clock", "phase", "initial");
        component.AddAttribute("phase", "initial");
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

    private static ValueTask<ComponentInstance> UnusedFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Factory should not run.");

    private static ValueTask<ComponentInstance> ConflictingFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Conflicting factory should not run.");
}
