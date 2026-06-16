using FluxFlow.Components.Storage;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;

namespace FluxFlow.Components.Storage.Tests;

/// <summary>
/// Builds storage operation runtime nodes through the registry/factory while
/// exposing a `$resources` view that contains a storage.store resource node,
/// so the factory's GetResource&lt;IStorageStoreHandle&gt; lookup resolves.
/// </summary>
internal static class StorageResourceTestContext
{
    public const string StoreName = "main-store";

    public static RuntimeNodeFactoryRegistry CreateRegistry(TimeProvider? clock = null)
        => new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options =>
            {
                // The store factory is unused this step but RegisterStorageComponents
                // still accepts one; the storage.store component holds config only.
                if (clock is not null)
                {
                    options.UseClock(clock);
                }
            });

    /// <summary>
    /// Builds a storage.store resource node through the registry-registered
    /// factory and returns a resources view keyed by its node name so the
    /// operation factories can resolve the handle.
    /// </summary>
    public static IReadOnlyDictionary<NodeName, RuntimeNode> CreateResources(
        RuntimeNodeFactoryRegistry registry,
        object? configuration = null,
        string storeName = StoreName)
    {
        registry.TryGetFactory(StorageComponentTypes.Store, out var factory)
            .ShouldBeTrue();

        var resourceNodes = new Dictionary<NodeName, RuntimeNode>();
        var definition = new NodeDefinition
        {
            Type = StorageComponentTypes.Store,
            Configuration = ToConfiguration(configuration ?? new { })
        };

        // WorkflowName null => resource scope, mirroring ApplicationRuntimeBuilder.
        var context = new RuntimeNodeFactoryContext(
            new NodeName(storeName),
            definition,
            WorkflowName: null,
            resourceNodes);
        var node = factory(context);
        resourceNodes[new NodeName(storeName)] = node;
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
