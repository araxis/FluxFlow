using FluxFlow.Composition.Definition;
using System.Text.Json;
using System.Reflection;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentActivationContextTests
{
    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"  \"")]
    public void Malformed_resource_reference_is_rejected_by_the_factory_context(string json)
    {
        using var document = JsonDocument.Parse(json);
        var context = new ComponentActivationContext(new TestServiceProvider("unused", new object()),
            "Main", "Node", new ComponentDefinition("sample",
                [new KeyValuePair<string, JsonElement>("Client", document.RootElement.Clone())]));
        var error = Should.Throw<InvalidOperationException>(() => context.GetResource<object>("Client"));
        error.Message.ShouldContain("Main.Node");
        error.Message.ShouldContain("Client");
        Should.Throw<InvalidOperationException>(() => context.GetRequiredResource<object>("Client"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Absent_optional_resource_is_allowed_but_required_resource_is_rejected(bool explicitNull)
    {
        var context = new ComponentActivationContext(new TestServiceProvider("unused", new object()),
            "Main", "Node", new ComponentDefinition("sample", explicitNull
                ? [new KeyValuePair<string, JsonElement>("Client", JsonSerializer.SerializeToElement<object?>(null))]
                : []));
        context.GetResource<object>("Client").ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => context.GetRequiredResource<object>("Client"));
    }

    [Theory]
    [InlineData("Client", "Resources.Client")]
    [InlineData("Mqtt.Client", "Resources.Mqtt.Client")]
    [InlineData("resources.Mqtt.Client", "Resources.Mqtt.Client")]
    [InlineData("Resources.Mqtt.Client", "Resources.Mqtt.Client")]
    public void Resource_references_resolve_without_searching_other_groups(string reference, string key)
    {
        var value = new object();
        var context = new ComponentActivationContext(new TestServiceProvider(key, value), "main", "publish",
            new ComponentDefinition("sample", [Property("Client", reference)]));
        context.GetRequiredResource<object>("Client").ShouldBeSameAs(value);
        context.GetResource<object>("Client").ShouldBeSameAs(value);
        var missing = new ComponentActivationContext(new TestServiceProvider("Resources.other.Client", value), "main", "publish",
            new ComponentDefinition("sample", [Property("Client", reference)]));
        var error = Should.Throw<InvalidOperationException>(() => missing.GetRequiredResource<object>("Client"));
        error.Message.ShouldContain("main.publish");
        error.Message.ShouldContain(key);
    }

    [Fact]
    public void Output_policy_arrays_do_not_leak_into_component_option_binding()
    {
        var definition = new ComponentDefinition("sample",
        [
            Property("MaximumPendingRequests", 17),
            Property("DiscardUnmatched", new[] { "Output" }),
        ]);
        var context = new ComponentActivationContext(new TestServiceProvider("unused", new object()),
            "Main", "Processor", definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });

        var options = context.BindConfiguration<SampleOptions>();

        options.Name.ShouldBe("Processor");
        options.MaximumPendingRequests.ShouldBe(17);
        options.Client.ShouldBeNull();
        context.Component.ShouldBeSameAs(definition);
        definition.Properties["DiscardUnmatched"].EnumerateArray().Select(item => item.GetString()).ShouldBe(["Output"]);
    }

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
        var context = new ComponentActivationContext(
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
        var context = new ComponentActivationContext(
            new TestServiceProvider("unused", new object()),
            "Orders",
            "Map",
            new ComponentDefinition("data.map", [Property("Name", "Custom")]));

        context.BindConfiguration<SampleOptions>().Name.ShouldBe("Custom");
    }

    [Fact]
    public void Activation_context_shares_only_its_private_default_serializer_options()
    {
        var services = new TestServiceProvider("unused", new object());
        var definition = new ComponentDefinition("sample");
        var first = new ComponentActivationContext(
            services,
            "Orders",
            "First",
            definition);
        var second = new ComponentActivationContext(
            services,
            "Orders",
            "Second",
            definition);
        var customOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var custom = new ComponentActivationContext(
            services,
            "Orders",
            "Custom",
            definition,
            customOptions);
        var field = typeof(ComponentActivationContext)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.FieldType == typeof(JsonSerializerOptions));

        var firstOptions = field.GetValue(first).ShouldBeOfType<JsonSerializerOptions>();
        var secondOptions = field.GetValue(second).ShouldBeOfType<JsonSerializerOptions>();
        var actualCustomOptions = field.GetValue(custom).ShouldBeOfType<JsonSerializerOptions>();

        firstOptions.ShouldBeSameAs(secondOptions);
        firstOptions.ShouldNotBeSameAs(customOptions);
        actualCustomOptions.ShouldBeSameAs(customOptions);
    }

    [Fact]
    public async Task Processing_profiles_use_the_DI_mapper_and_preserve_explicit_property_overrides()
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
        var context = new ComponentActivationContext(services, "Orders", "Map",
            new ComponentDefinition("sample", [Property("Processing", "Resources.Processing.ParallelOrdered"), Property("boundedCapacity", 99)]));
        bound = context.BindConfiguration<ProcessingOptions>(CompositionProcessingCapabilities.ParallelPreservingOrder);
        var descriptor = await context.BindAsync(new CompletedNode());

        bound.ShouldNotBeNull();
        bound.BoundedCapacity.ShouldBe(99);
        bound.MaxDegreeOfParallelism.ShouldBe(3);
        bound.EnsureOrdered.ShouldBeTrue();
        mapper.CallCount.ShouldBe(1);
        await descriptor.DisposeAsync();
    }

    [Fact]
    public async Task Unsupported_parallel_processing_is_rejected_before_node_construction()
    {
        var services = new TestServiceProvider(
            "Resources.Processing.Parallel",
            new CompositionProcessingProfile
            {
                Mode = CompositionProcessingMode.Parallel,
                Order = CompositionProcessingOrder.Relaxed
            });
        var factoryCalled = false;
        var context = new ComponentActivationContext(services, "Orders", "State",
            new ComponentDefinition("state.reduce", [Property("processing", "Resources.Processing.Parallel")]));
        var exception = Should.Throw<InvalidOperationException>(() =>
        {
            context.BindConfiguration<ProcessingOptions>();
            factoryCalled = true;
        });
        await Task.CompletedTask;

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
