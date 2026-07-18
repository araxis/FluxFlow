using System.Text.Json;
using FluxFlow.Composition.Model;
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
        context.BindConfiguration<SampleOptions>().MaximumPendingRequests.ShouldBe(32);
    }

    private static KeyValuePair<string, JsonElement> Property<T>(string name, T value)
        => new(name, JsonSerializer.SerializeToElement(value));

    private sealed record SampleOptions
    {
        public int MaximumPendingRequests { get; init; }
    }

    private sealed class TestServiceProvider(object key, object service) :
        IServiceProvider,
        IKeyedServiceProvider
    {
        public object? GetService(Type serviceType) => null;

        public object? GetKeyedService(Type serviceType, object? serviceKey)
            => serviceType == typeof(object) && Equals(serviceKey, key) ? service : null;

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
            => GetKeyedService(serviceType, serviceKey)
               ?? throw new InvalidOperationException("Missing keyed test service.");
    }
}
