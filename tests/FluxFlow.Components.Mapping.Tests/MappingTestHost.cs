using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;

namespace FluxFlow.Components.Mapping.Tests;

internal static class MappingTestHost
{
    public static RuntimeNodeFactoryContext CreateContext(object configuration)
        => new(
            new NodeName("mapper"),
            new NodeDefinition
            {
                Type = MappingComponentTypes.Mapper,
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
