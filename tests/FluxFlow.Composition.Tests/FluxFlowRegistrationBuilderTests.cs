using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class FluxFlowRegistrationBuilderTests
{
    [Fact]
    public void AddFluxFlowComponents_returns_flat_builder_with_original_services_and_one_catalog_registration()
    {
        var services = new ServiceCollection();

        var builder = services.AddFluxFlowComponents();

        builder.Services.ShouldBeSameAs(services);
        services.Count(registration => registration.ServiceType == typeof(ComponentCatalog))
            .ShouldBe(1);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>().Descriptors.ShouldBeEmpty();
    }

    [Fact]
    public void AddFluxFlowComponents_rejects_null_services()
    {
        var exception = Should.Throw<ArgumentNullException>(() =>
            FluxFlowRegistrationExtensions.AddFluxFlowComponents(null!));

        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddRuntimeComponent_returns_same_builder_and_invokes_configuration_once()
    {
        var builder = new ServiceCollection().AddFluxFlowComponents();
        var invocationCount = 0;
        RuntimeComponentRegistrationBuilder? configured = null;

        var returned = builder.AddRuntimeComponent("test.component", component =>
        {
            invocationCount++;
            configured = component;
            ConfigureRuntime(component);
        });

        returned.ShouldBeSameAs(builder);
        invocationCount.ShouldBe(1);
        configured.ShouldNotBeNull();
    }

    [Fact]
    public void AddRuntimeComponent_registers_the_complete_runtime_descriptor()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents()
            .AddRuntimeComponent(" test.component ", ConfigureRuntime);

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var catalog = provider.GetRequiredService<ComponentCatalog>();

        descriptor.Type.ShouldBe("test.component");
        descriptor.ProcessingCapabilities.ShouldBe(
            CompositionProcessingCapabilities.ParallelRelaxedOrder);
        descriptor.Inputs.Keys.ShouldBe(["Input", "Signal"]);
        descriptor.Inputs["Input"].MessageType.ShouldBe(typeof(string));
        descriptor.Inputs["Signal"].Kind.ShouldBe(ComponentPortKind.Signal);
        descriptor.Outputs.Keys.ShouldBe(["Output", "Events"]);
        descriptor.Outputs["Output"].MessageType.ShouldBe(typeof(int));
        descriptor.Outputs["Output"].LinkCardinality
            .ShouldBe(ComponentPortLinkCardinality.Single);
        descriptor.Options.Keys.ShouldBe(["enabled"]);
        descriptor.Options["enabled"].ValueType.ShouldBe(typeof(bool));
        descriptor.Options["enabled"].IsRequired.ShouldBeTrue();
        descriptor.Resources.Keys.ShouldBe(["clock"]);
        descriptor.Resources["clock"].ServiceType.ShouldBe(typeof(TimeProvider));
        descriptor.Resources["clock"].IsRequired.ShouldBeTrue();
        catalog.Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
    }

    [Fact]
    public void Equivalent_runtime_registration_is_idempotent_across_flat_builders()
    {
        var services = new ServiceCollection();

        services.AddFluxFlowComponents()
            .AddRuntimeComponent("test.component", ConfigureRuntime)
            .AddRuntimeComponent("test.component", ConfigureRuntime);
        services.AddFluxFlowComponents()
            .AddRuntimeComponent("test.component", ConfigureRuntime);

        services.Count(registration => registration.ServiceType == typeof(ComponentCatalog))
            .ShouldBe(1);
        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        provider.GetRequiredService<ComponentCatalog>()
            .Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
    }

    [Fact]
    public void Conflicting_runtime_registration_fails_immediately_without_partial_state()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents()
            .AddRuntimeComponent("test.component", ConfigureRuntime);
        var original = services
            .Where(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .Select(registration => registration.ImplementationInstance)
            .OfType<ComponentDescriptor>()
            .ShouldHaveSingleItem();

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddRuntimeComponent("test.component", component =>
            {
                ConfigureRuntime(component);
                component.UseProcessing(CompositionProcessingCapabilities.Sequential);
            }));

        exception.Message.ShouldContain("test.component");
        exception.Message.ShouldContain("conflicting descriptor registration");
        services
            .Where(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .Select(registration => registration.ImplementationInstance)
            .OfType<ComponentDescriptor>()
            .ShouldHaveSingleItem().ShouldBeSameAs(original);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>()
            .Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(original);
    }

    [Fact]
    public void Runtime_registration_requires_factory_without_appending_partial_state()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddRuntimeComponent("test.component", component =>
                component.AddInput<string>("Input")));

        exception.Message.ShouldContain("test.component");
        exception.Message.ShouldContain(nameof(RuntimeComponentRegistrationBuilder.UseFactory));
        services.ShouldNotContain(registration =>
            registration.ServiceType == typeof(ComponentDescriptor));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>().Descriptors.ShouldBeEmpty();
    }

    [Fact]
    public void Runtime_registration_rejects_null_builder_type_and_configuration()
    {
        var builder = new ServiceCollection().AddFluxFlowComponents();

        Should.Throw<ArgumentNullException>(() =>
                FluxFlowRegistrationExtensions.AddRuntimeComponent(
                    null!,
                    "test.component",
                    ConfigureRuntime))
            .ParamName.ShouldBe("builder");
        Should.Throw<ArgumentException>(() =>
                builder.AddRuntimeComponent(" ", ConfigureRuntime))
            .ParamName.ShouldBe("type");
        Should.Throw<ArgumentNullException>(() =>
                builder.AddRuntimeComponent("test.component", null!))
            .ParamName.ShouldBe("configure");
    }

    [Fact]
    public void Separate_flat_builders_compose_into_one_ordinal_catalog()
    {
        var services = new ServiceCollection();

        services.AddFluxFlowComponents()
            .AddRuntimeComponent("test.zulu", ConfigureMinimal);
        services.AddFluxFlowComponents()
            .AddRuntimeComponent("test.alpha", ConfigureMinimal);

        services.Count(registration => registration.ServiceType == typeof(ComponentCatalog))
            .ShouldBe(1);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>()
            .Descriptors.Select(descriptor => descriptor.Type)
            .ShouldBe(["test.alpha", "test.zulu"]);
    }

    [Fact]
    public void Standalone_flat_runtime_usage_compiles_without_Designer_or_Engine()
    {
        IServiceCollection services = new ServiceCollection();
        FluxFlowRegistrationBuilder registration = services.AddFluxFlowComponents();

        FluxFlowRegistrationBuilder returned = registration.AddRuntimeComponent(
            "test.component",
            component => component.UseFactory(UnusedFactory));

        returned.ShouldBeSameAs(registration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>().Components.Keys
            .ShouldBe(["test.component"]);
    }

    private static void ConfigureRuntime(RuntimeComponentRegistrationBuilder component)
    {
        component.UseFactory(UnusedFactory);
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component.AddInput<string>("Input");
        component.AddSignalInput("Signal");
        component.AddOutput<int>("Output", ComponentPortLinkCardinality.Single);
        component.AddOption<bool>("enabled", isRequired: true);
        component.AddResource<TimeProvider>("clock", isRequired: true);
    }

    private static void ConfigureMinimal(RuntimeComponentRegistrationBuilder component)
        => component.UseFactory(UnusedFactory);

    private static ValueTask<ComponentInstance> UnusedFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Factory should not run.");
}
