using FluxFlow.Components.Storage;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;

namespace FluxFlow.Components.Storage.Tests;

/// <summary>
/// Builds storage operation runtime nodes through the registry/factory while
/// exposing a `$resources` view that contains a storage.store resource node,
/// so the factory's GetResource&lt;IStorageStoreHandle&gt; lookup resolves. The
/// store factory the storage.store node opens on host ConnectAsync can be supplied
/// per test; when omitted the default MissingStorageStoreFactory is used (its
/// OpenAsync throws, so a connect attempt faults the resource without opening).
/// </summary>
internal static class StorageResourceTestContext
{
    public const string StoreName = "main-store";

    public static RuntimeNodeFactoryRegistry CreateRegistry(
        TimeProvider? clock = null,
        IStorageStoreFactory? storeFactory = null)
        => new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options =>
            {
                // The storage.store node owns the store; it opens the factory on the
                // host-driven ConnectAsync. Tests that never connect can omit it.
                if (storeFactory is not null)
                {
                    options.UseStoreFactory(storeFactory);
                }

                if (clock is not null)
                {
                    options.UseClock(clock);
                }
            });

    /// <summary>
    /// Resolves the storage.store resource handle from a resources view so tests
    /// can drive the host-API connect/disconnect lifecycle.
    /// </summary>
    public static IStorageStoreHandle ResolveHandle(
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        string storeName = StoreName)
        => resources[new NodeName(storeName)].Node
            .ShouldBeAssignableTo<IStorageStoreHandle>()!;

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

    /// <summary>
    /// Builds a storage operation runtime node from the same registry/resources view
    /// that owns the storage.store handle, so the op node borrows the very store the
    /// host opens through that handle's ConnectAsync.
    /// </summary>
    public static RuntimeNode CreateOperationNode(
        RuntimeNodeFactoryRegistry registry,
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        NodeType type,
        object configuration)
    {
        registry.TryGetFactory(type, out var factory).ShouldBeTrue();
        return factory(CreateContext(type, configuration, resources));
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
