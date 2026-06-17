using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;

namespace FluxFlow.Components.Mqtt.Tests;

/// <summary>
/// Builds publish/subscribe runtime nodes through the registry/factory while
/// exposing a `$resources` view that contains an mqtt.connection resource node,
/// so the factory's GetResource&lt;IMqttConnectionHandle&gt; lookup resolves.
/// </summary>
internal static class MqttResourceTestContext
{
    public const string ConnectionName = "main-broker";

    public static RuntimeNodeFactoryRegistry CreateRegistry(
        TimeProvider? clock = null,
        IMqttClientFactory? clientFactory = null)
        => new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options =>
            {
                // The connection node owns the client; the module now requires a
                // factory. Tests that never connect can pass a stub that throws.
                options.UseClientFactory(clientFactory ?? new ThrowingMqttClientFactory());
                if (clock is not null)
                {
                    options.UseClock(clock);
                }
            });

    /// <summary>
    /// Resolves the mqtt.connection resource handle from a resources view so tests
    /// can drive the host-API connect/disconnect lifecycle.
    /// </summary>
    public static IMqttConnectionHandle ResolveHandle(
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        string connectionName = ConnectionName)
        => resources[new NodeName(connectionName)].Node
            .ShouldBeAssignableTo<IMqttConnectionHandle>()!;

    /// <summary>
    /// Builds an mqtt.connection resource node through the registry-registered
    /// factory and returns a resources view keyed by its node name so
    /// publish/subscribe factories can resolve the handle.
    /// </summary>
    public static IReadOnlyDictionary<NodeName, RuntimeNode> CreateResources(
        RuntimeNodeFactoryRegistry registry,
        object? configuration = null,
        string connectionName = ConnectionName)
    {
        registry.TryGetFactory(MqttComponentTypes.Connection, out var factory)
            .ShouldBeTrue();

        var resourceNodes = new Dictionary<NodeName, RuntimeNode>();
        var definition = new NodeDefinition
        {
            Type = MqttComponentTypes.Connection,
            Configuration = ToConfiguration(configuration ?? new
            {
                profile = new { name = connectionName, host = "localhost", port = 1883 }
            })
        };

        // WorkflowName null => resource scope, mirroring ApplicationRuntimeBuilder.
        var context = new RuntimeNodeFactoryContext(
            new NodeName(connectionName),
            definition,
            WorkflowName: null,
            resourceNodes);
        var node = factory(context);
        resourceNodes[new NodeName(connectionName)] = node;
        return resourceNodes;
    }

    public static RuntimeNodeFactoryContext CreateContext(
        NodeType type,
        object configuration,
        IReadOnlyDictionary<NodeName, RuntimeNode> resources)
        => new(
            new NodeName("node"),
            new NodeDefinition
            {
                Type = type,
                Configuration = ToConfiguration(configuration)
            },
            "main",
            resources);

    public static Dictionary<string, JsonElement> ToConfiguration(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        return root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
