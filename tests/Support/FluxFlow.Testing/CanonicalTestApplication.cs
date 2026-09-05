using System.Text.Json;
using FluxFlow.Composition.Model;

namespace FluxFlow.Testing;

public static class CanonicalTestApplication
{
    public static ApplicationDefinition SingleComponent(
        string componentType,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        string workflowName = "main",
        string componentName = "node")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);

        var resourceDefinitions = resources?.Select(name =>
            KeyValuePair.Create<string, ResourceDefinition>(
                name,
                new ResourceInstanceDefinition("host.external")));
        var component = new ComponentDefinition(
            componentType,
            properties?.Select(property => KeyValuePair.Create(
                property.Key,
                JsonSerializer.SerializeToElement(property.Value))));
        return new ApplicationDefinition(
            resourceDefinitions,
            [KeyValuePair.Create(
                workflowName,
                new WorkflowDefinition(
                    [KeyValuePair.Create(componentName, component)]))]);
    }

    public static IReadOnlyDictionary<string, object?> Properties(
        params (string Name, object? Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.ToDictionary(
            static value => value.Name,
            static value => value.Value,
            StringComparer.Ordinal);
    }
}
