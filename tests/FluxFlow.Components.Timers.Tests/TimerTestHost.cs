using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;

namespace FluxFlow.Components.Timers.Tests;

internal static class TimerTestHost
{
    public static RuntimeNodeFactoryContext CreateContext(
        NodeType type,
        object configuration,
        string name = "timer")
        => new(
            new NodeName(name),
            new NodeDefinition
            {
                Type = type,
                Configuration = ToConfiguration(configuration)
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());

    private static Dictionary<string, JsonElement> ToConfiguration(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        return root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
