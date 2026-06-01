using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;

namespace FluxFlow.Components.FileSystem.Tests;

internal static class FileSystemTestHost
{
    public static RuntimeNodeFactoryContext CreateContext(object configuration)
        => CreateContext(FileSystemComponentTypes.FileWrite, configuration, "writer");

    public static RuntimeNodeFactoryContext CreateContext(
        NodeType nodeType,
        object configuration,
        string nodeName)
        => new(
            new NodeName(nodeName),
            new NodeDefinition
            {
                Type = nodeType,
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
