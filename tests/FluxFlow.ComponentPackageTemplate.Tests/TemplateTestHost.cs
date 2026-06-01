using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;

namespace FluxFlow.ComponentPackageTemplate.Tests;

internal static class TemplateTestHost
{
    public static RuntimeNodeFactoryContext CreateContext(object configuration)
        => new(
            new NodeName("enrich"),
            new NodeDefinition
            {
                Type = TemplateComponentTypes.Enrich,
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
