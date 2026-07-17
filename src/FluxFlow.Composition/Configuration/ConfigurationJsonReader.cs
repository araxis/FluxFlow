using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Composition;

internal static class ConfigurationJsonReader
{
    public static JsonNode? Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var children = configuration.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return configuration is IConfigurationSection section
                ? CreateScalar(section.Value)
                : null;
        }

        if (LooksLikeArray(children))
        {
            var array = new JsonArray();
            foreach (var child in children.OrderBy(
                         child => int.Parse(child.Key, CultureInfo.InvariantCulture)))
            {
                array.Add(Read(child));
            }

            return array;
        }

        var obj = new JsonObject();
        foreach (var child in children)
            obj[child.Key] = Read(child);

        return obj;
    }

    private static JsonNode? CreateScalar(string? value)
    {
        if (value is null)
            return null;

        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(value);
        }
    }

    private static bool LooksLikeArray(IReadOnlyList<IConfigurationSection> children)
    {
        if (children.Count == 0)
            return false;

        var indexes = new List<int>(children.Count);
        foreach (var child in children)
        {
            if (!int.TryParse(
                    child.Key,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                return false;
            }

            indexes.Add(index);
        }

        indexes.Sort();
        for (var index = 0; index < indexes.Count; index++)
        {
            if (indexes[index] != index)
                return false;
        }

        return true;
    }
}
