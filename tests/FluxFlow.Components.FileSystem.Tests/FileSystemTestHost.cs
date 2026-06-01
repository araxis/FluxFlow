using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;

namespace FluxFlow.Components.FileSystem.Tests;

internal static class FileSystemTestHost
{
    public static RuntimeNodeFactoryContext CreateContext(object configuration)
        => new(
            new NodeName("writer"),
            new NodeDefinition
            {
                Type = FileSystemComponentTypes.FileWrite,
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
