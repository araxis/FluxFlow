using System.Text.Json;
using FluxFlow.Composition.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CompositionNodeFactoryContextTests
{
    [Fact]
    public void Canonical_component_properties_bind_options_and_resource_references()
    {
        var resource = new object();
        var services = new TestServiceProvider("Resources.Mqtt.Client1", resource);
        var definition = new ComponentDefinition(
            "sample",
            [
                Property("Client", "Resources.Mqtt.Client1"),
                Property("MaximumPendingRequests", 32)
            ]);
        var context = new CompositionNodeFactoryContext(
            services,
            "Orders",
            "Client",
            definition);

        context.GetRequiredResource<object>("Client").ShouldBeSameAs(resource);
        var options = context.BindConfiguration<SampleOptions>();
        options.Name.ShouldBe("Client");
        options.MaximumPendingRequests.ShouldBe(32);
        context.Component.ShouldBeSameAs(definition);
    }

    [Fact]
    public void Explicit_configured_name_overrides_the_component_identity_default()
    {
        var context = new CompositionNodeFactoryContext(
            new TestServiceProvider("unused", new object()),
            "Orders",
            "Map",
            new ComponentDefinition("data.map", [Property("Name", "Custom")]));

        context.BindConfiguration<SampleOptions>().Name.ShouldBe("Custom");
    }

    [Fact]
    public void Legacy_context_preserves_explicit_name_and_separate_resource_slots()
    {
        var resource = new object();
#pragma warning disable CS0618
        var context = new CompositionNodeFactoryContext(
            new TestServiceProvider("resource-key", resource),
            "Orders",
            "Legacy",
            new NodeDefinition
            {
                Type = "sample",
                Configuration = new Dictionary<string, JsonElement>
                {
                    ["Name"] = JsonSerializer.SerializeToElement("Configured"),
                    ["Client"] = JsonSerializer.SerializeToElement("diagnostic-value")
                },
                Resources = new Dictionary<string, string>
                {
                    ["Client"] = "resource-key"
                }
            });
#pragma warning restore CS0618

        var options = context.BindConfiguration<SampleOptions>();
        options.Name.ShouldBe("Configured");
        options.Client.ShouldBe("diagnostic-value");
        context.GetRequiredResource<object>("Client").ShouldBeSameAs(resource);
    }

    [Fact]
    public async Task Processing_profiles_use_the_DI_mapper_and_preserve_explicit_legacy_overrides()
    {
        var profile = new CompositionProcessingProfile
        {
            Mode = CompositionProcessingMode.Parallel,
            Order = CompositionProcessingOrder.Preserve,
            Buffer = CompositionProcessingBuffer.Large
        };
        var mapper = new TestProcessingMapper(new CompositionProcessingSettings(64, 3, true));
        var services = new TestServiceProvider(
            "Resources.Processing.ParallelOrdered",
            profile,
            mapper);
        ProcessingOptions? bound = null;
        var registration = new CompositionNodeRegistration(
            "sample",
            context =>
            {
                bound = context.BindConfiguration<ProcessingOptions>();
                return ValueTask.FromResult(ComposedNode.Create(new CompletedNode()));
            },
            inputs: null,
            outputs: null,
            processingCapabilities: CompositionProcessingCapabilities.ParallelPreservingOrder);

        var descriptor = await registration.Factory(new CompositionNodeFactoryContext(
            services,
            "Orders",
            "Map",
            new ComponentDefinition(
                "sample",
                [
                    Property("Processing", "Resources.Processing.ParallelOrdered"),
                    Property("BoundedCapacity", 99)
                ])));

        bound.ShouldNotBeNull();
        bound.BoundedCapacity.ShouldBe(99);
        bound.MaxDegreeOfParallelism.ShouldBe(3);
        bound.EnsureOrdered.ShouldBeTrue();
        mapper.CallCount.ShouldBe(1);
        await descriptor.DisposeAsync();
    }

    [Fact]
    public async Task Unsupported_parallel_processing_is_rejected_before_factory_execution()
    {
        var services = new TestServiceProvider(
            "Resources.Processing.Parallel",
            new CompositionProcessingProfile
            {
                Mode = CompositionProcessingMode.Parallel,
                Order = CompositionProcessingOrder.Relaxed
            });
        var factoryCalled = false;
        var registration = new CompositionNodeRegistration(
            "state.reduce",
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult(ComposedNode.Create(new CompletedNode()));
            });

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await registration.Factory(new CompositionNodeFactoryContext(
                services,
                "Orders",
                "State",
                new ComponentDefinition(
                    "state.reduce",
                    [Property("processing", "Resources.Processing.Parallel")]))));

        exception.Message.ShouldContain("Orders.State");
        exception.Message.ShouldContain("does not support");
        factoryCalled.ShouldBeFalse();
    }

    private static KeyValuePair<string, JsonElement> Property<T>(string name, T value)
        => new(name, JsonSerializer.SerializeToElement(value));

    private sealed record SampleOptions
    {
        public string? Name { get; init; }

        public string? Client { get; init; }

        public int MaximumPendingRequests { get; init; }
    }

    private sealed record ProcessingOptions
    {
        public int BoundedCapacity { get; init; }

        public int MaxDegreeOfParallelism { get; init; }

        public bool EnsureOrdered { get; init; }
    }

    private sealed class TestServiceProvider(
        object key,
        object service,
        object? unkeyedService = null) :
        IServiceProvider,
        IKeyedServiceProvider
    {
        public object? GetService(Type serviceType)
            => unkeyedService is not null && serviceType.IsInstanceOfType(unkeyedService)
                ? unkeyedService
                : null;

        public object? GetKeyedService(Type serviceType, object? serviceKey)
            => serviceType.IsInstanceOfType(service) && Equals(serviceKey, key) ? service : null;

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
            => GetKeyedService(serviceType, serviceKey)
               ?? throw new InvalidOperationException("Missing keyed test service.");
    }

    private sealed class TestProcessingMapper(CompositionProcessingSettings settings) :
        ICompositionProcessingProfileMapper
    {
        public int CallCount { get; private set; }

        public CompositionProcessingSettings Map(CompositionProcessingProfile profile)
        {
            CallCount++;
            return settings;
        }
    }

    private sealed class CompletedNode : IFlowNode
    {
        public Task Completion => Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
