using System.Text.Json;
using FluxFlow.Composition.Addressing;

namespace FluxFlow.Components.Mqtt.Composition;

internal static class MqttCompositionResourceValidator
{
    internal static void ValidateProperties(
        MqttIndexedResource resource,
        params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unknown = resource.Definition.Properties.Keys
            .Where(property => !names.Contains(property))
            .OrderBy(static property => property, StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"MQTT resource '{resource.Address}' has unsupported properties: " +
                string.Join(", ", unknown));
        }
    }

    internal static void ValidateObjectProperties(
        JsonElement value,
        ApplicationAddress owner,
        string propertyName,
        params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unknown = value.EnumerateObject()
            .Select(static property => property.Name)
            .Where(property => !names.Contains(property))
            .OrderBy(static property => property, StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' property '{propertyName}' has unsupported properties: " +
                string.Join(", ", unknown));
        }
    }

    internal static InvalidOperationException InvalidShape(
        ApplicationAddress owner,
        string propertyName,
        string expected)
        => new($"MQTT resource '{owner}' property '{propertyName}' must be {expected}.");
}
