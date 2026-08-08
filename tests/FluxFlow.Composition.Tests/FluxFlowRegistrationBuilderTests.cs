using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Authoring;
using FluxFlow.Nodes;
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
    public void Advanced_dynamic_registration_returns_same_builder_and_invokes_configuration_once()
    {
        var builder = new ServiceCollection().AddFluxFlowComponents();
        var advanced = builder.Advanced;
        var invocationCount = 0;
        RuntimeComponentRegistrationBuilder? configured = null;

        var returned = advanced.AddDynamicComponent("test.component", component =>
        {
            invocationCount++;
            configured = component;
            ConfigureRuntime(component);
        });

        returned.ShouldBeSameAs(advanced);
        invocationCount.ShouldBe(1);
        configured.ShouldNotBeNull();
    }

    [Fact]
    public void Advanced_dynamic_registration_registers_the_complete_runtime_descriptor()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents()
            .Advanced.AddDynamicComponent(" test.component ", ConfigureRuntime);

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
            .Advanced.AddDynamicComponent("test.component", ConfigureRuntime)
            .AddDynamicComponent("test.component", ConfigureRuntime);
        services.AddFluxFlowComponents()
            .Advanced.AddDynamicComponent("test.component", ConfigureRuntime);

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
            .Advanced.AddDynamicComponent("test.component", ConfigureRuntime);
        var original = services
            .Where(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .Select(registration => registration.ImplementationInstance)
            .OfType<ComponentDescriptor>()
            .ShouldHaveSingleItem();

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddDynamicComponent("test.component", component =>
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
        var builder = services.AddFluxFlowComponents().Advanced;

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddDynamicComponent("test.component", component =>
                component.UseProcessing(CompositionProcessingCapabilities.Sequential)));

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
        var builder = new ServiceCollection().AddFluxFlowComponents().Advanced;

        Should.Throw<NullReferenceException>(() =>
                ((AdvancedFluxFlowRegistrationBuilder)null!).AddDynamicComponent(
                    "test.component",
                    ConfigureRuntime));
        Should.Throw<ArgumentException>(() =>
                builder.AddDynamicComponent(" ", ConfigureRuntime))
            .ParamName.ShouldBe("type");
        Should.Throw<ArgumentNullException>(() =>
                builder.AddDynamicComponent("test.component", null!))
            .ParamName.ShouldBe("configure");
    }

    [Fact]
    public void Separate_flat_builders_compose_into_one_ordinal_catalog()
    {
        var services = new ServiceCollection();

        services.AddFluxFlowComponents()
            .Advanced.AddDynamicComponent("test.zulu", ConfigureMinimal);
        services.AddFluxFlowComponents()
            .Advanced.AddDynamicComponent("test.alpha", ConfigureMinimal);

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

        AdvancedFluxFlowRegistrationBuilder advanced = registration.Advanced;
        AdvancedFluxFlowRegistrationBuilder returned = advanced.AddDynamicComponent(
            "test.component",
            component => component.UseFactory(CreateNode));

        returned.ShouldBeSameAs(advanced);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>().Components.Keys
            .ShouldBe(["test.component"]);
    }

    private static void ConfigureRuntime(RuntimeComponentRegistrationBuilder component)
    {
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component
            .UseFactory(CreateNode)
            .HasInput("Input", static node => node.Input)
            .HasSignalInput("Signal", static node => node.Signal)
            .HasOutput(
                "Output",
                static node => node.Output,
                ComponentPortLinkCardinality.Single)
            .HasEvents("Events", static node => node.Events);
        component.AddOption<bool>("enabled", isRequired: true);
        component.AddResource<TimeProvider>("clock", isRequired: true);
    }

    [Fact]
    public void Exact_contract_registration_is_idempotent_and_reuses_the_owned_descriptor()
    {
        var contract = ComponentContract.Create(
            " test.component ",
            ConfigureRuntime,
            static component => new OutputComponentHandle<int>(component, "Output", "Events"));
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();

        builder.AddComponent(contract).ShouldBeSameAs(builder);
        builder.AddComponent(contract).ShouldBeSameAs(builder);
        services.AddFluxFlowComponents().AddComponent(contract);

        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);
        services.Count(registration => registration.ServiceType == typeof(ComponentCatalog))
            .ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentDescriptor>().ShouldBeSameAs(contract.Descriptor);
        provider.GetRequiredService<ComponentCatalog>().Descriptors
            .ShouldHaveSingleItem().ShouldBeSameAs(contract.Descriptor);
        contract.Type.ShouldBe("test.component");
    }

    [Fact]
    public void Distinct_contract_registration_for_the_same_type_conflicts_without_partial_state()
    {
        var first = ComponentContract.Create(
            "test.component",
            ConfigureRuntime,
            static component => new OutputComponentHandle<int>(component, "Output", "Events"));
        var second = ComponentContract.Create(
            "test.component",
            ConfigureRuntime,
            static component => new OutputComponentHandle<int>(component, "Output", "Events"));
        second.Descriptor.ShouldNotBeSameAs(first.Descriptor);
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents().AddComponent(first);
        var originalRegistrations = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddComponent(second));

        exception.Message.ShouldContain("test.component");
        exception.Message.ShouldContain("conflicting descriptor registration");
        services.Count.ShouldBe(originalRegistrations.Length);
        for (var index = 0; index < originalRegistrations.Length; index++)
            services[index].ShouldBeSameAs(originalRegistrations[index]);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentDescriptor>().ShouldBeSameAs(first.Descriptor);
        provider.GetRequiredService<ComponentCatalog>().Descriptors
            .ShouldHaveSingleItem().ShouldBeSameAs(first.Descriptor);
    }

    private static void ConfigureMinimal(RuntimeComponentRegistrationBuilder component)
        => component.UseFactory(CreateNode);

    private static RegistrationNode CreateNode(ComponentActivationContext _) => new();

    private sealed class RegistrationNode : IFlowNode, IFlowSignalTarget
    {
        public BufferBlock<FlowMessage<string>> Input { get; } = new();

        public BufferBlock<FlowMessage<int>> Output { get; } = new();

        public BufferBlock<FlowEvent> Events { get; } = new();

        public IFlowSignalTarget Signal => this;

        public Task Completion { get; } = Task.CompletedTask;

        public void Complete()
        {
            Input.Complete();
            Output.Complete();
            Events.Complete();
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
        }

        public ValueTask<bool> SendAsync<T>(
            FlowMessage<T> signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }
}
