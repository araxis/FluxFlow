using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;

namespace FluxFlow.Components.Sources.Tests;

internal static class SourcesTestHost
{
    public static RuntimeNodeFactoryContext CreateContext(NodeType type, object configuration)
        => new(
            new NodeName("source"),
            new NodeDefinition
            {
                Type = type,
                Configuration = ToConfiguration(configuration)
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());

    public static Dictionary<string, JsonElement> ToConfiguration(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        return root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
