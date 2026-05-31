using System.Text.Json;

namespace FluxFlow.Engine.Scenarios;

public static class ScenarioStepConfigurationReader
{
    public static string ReadRequiredString(
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key)
    {
        var value = ReadString(configuration, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Scenario configuration value '{key}' is required and must be a string.");
        }

        return value;
    }

    public static string? ReadString(
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key)
    {
        if (!configuration.TryGetValue(key, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Scenario configuration value '{key}' must be a string.");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static bool ReadBoolOrDefault(
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key,
        bool defaultValue)
    {
        if (!configuration.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.True &&
            value.ValueKind != JsonValueKind.False)
        {
            throw new InvalidOperationException($"Scenario configuration value '{key}' must be a boolean.");
        }

        return value.GetBoolean();
    }

    public static int ReadIntOrDefault(
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key,
        int defaultValue,
        int minValue)
    {
        if (!configuration.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) ||
            result < minValue)
        {
            throw new InvalidOperationException(
                $"Scenario configuration value '{key}' must be an integer greater than or equal to {minValue}.");
        }

        return result;
    }

    public static IReadOnlyDictionary<string, string> ReadStringMap(
        IReadOnlyDictionary<string, JsonElement> configuration,
        string key)
    {
        if (!configuration.TryGetValue(key, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Scenario configuration value '{key}' must be an object.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[property.Name] = property.Value.GetString() ?? string.Empty;
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result[property.Name] = property.Value.GetBoolean().ToString();
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                result[property.Name] = property.Value.GetRawText();
                continue;
            }

            throw new InvalidOperationException(
                $"Scenario configuration value '{key}.{property.Name}' must be a string, boolean, or number.");
        }

        return result;
    }
}
