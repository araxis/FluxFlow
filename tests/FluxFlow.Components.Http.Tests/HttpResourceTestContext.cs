using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;

namespace FluxFlow.Components.Http.Tests;

/// <summary>
/// Builds request runtime nodes through the registry/factory while exposing a
/// `$resources` view that contains an http.client resource node, so the
/// factory's GetResource&lt;IHttpClientHandle&gt; lookup resolves.
/// </summary>
internal static class HttpResourceTestContext
{
    public const string ClientName = "main-client";

    public static RuntimeNodeFactoryRegistry CreateRegistry(
        Action<HttpComponentOptions>? configure = null)
        => new RuntimeNodeFactoryRegistry()
            .RegisterHttpComponents(options => configure?.Invoke(options));

    /// <summary>
    /// Resolves the http.client resource handle from a resources view so tests
    /// can drive the host-API connect/disconnect lifecycle.
    /// </summary>
    public static IHttpClientHandle ResolveHandle(
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        string clientName = ClientName)
        => resources[new NodeName(clientName)].Node
            .ShouldBeAssignableTo<IHttpClientHandle>()!;

    /// <summary>
    /// Builds an http.client resource node through the registry-registered
    /// factory and returns a resources view keyed by its node name so the
    /// request factory can resolve the handle.
    /// </summary>
    public static IReadOnlyDictionary<NodeName, RuntimeNode> CreateResources(
        RuntimeNodeFactoryRegistry registry,
        object? configuration = null,
        string clientName = ClientName)
    {
        registry.TryGetFactory(HttpComponentTypes.Client, out var factory)
            .ShouldBeTrue();

        var resourceNodes = new Dictionary<NodeName, RuntimeNode>();
        var definition = new NodeDefinition
        {
            Type = HttpComponentTypes.Client,
            Configuration = ToConfiguration(configuration ?? new { })
        };

        // WorkflowName null => resource scope, mirroring ApplicationRuntimeBuilder.
        var context = new RuntimeNodeFactoryContext(
            new NodeName(clientName),
            definition,
            WorkflowName: null,
            resourceNodes);
        var node = factory(context);
        resourceNodes[new NodeName(clientName)] = node;
        return resourceNodes;
    }

    public static RuntimeNodeFactoryContext CreateContext(
        NodeType type,
        object configuration,
        IReadOnlyDictionary<NodeName, RuntimeNode> resources)
        => new(
            new NodeName("request"),
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
