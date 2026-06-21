using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Composition;

public sealed class CompositionConfigurationLoader
{
    private readonly JsonSerializerOptions _serializerOptions;

    public CompositionConfigurationLoader(JsonSerializerOptions? serializerOptions = null)
    {
        _serializerOptions = serializerOptions ?? CompositionDefinitionJson.CreateSerializerOptions();
    }

    public const string DefaultSectionName = "FluxFlow:Composition";

    public CompositionDefinition Load(
        IConfiguration configuration,
        string sectionName = DefaultSectionName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(sectionName)
            ? configuration
            : configuration.GetSection(sectionName);

        return LoadSection(section);
    }

    public CompositionDefinition LoadSection(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration is IConfigurationSection section && !section.Exists())
            return new CompositionDefinition();

        try
        {
            var node = ReadConfiguration(configuration);
            if (node is null)
                return new CompositionDefinition();

            return node.Deserialize<CompositionDefinition>(_serializerOptions)
                ?? new CompositionDefinition();
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or ArgumentException)
        {
            throw new CompositionConfigurationException(
                "Composition configuration could not be loaded.",
                exception);
        }
    }

    private static JsonNode? ReadConfiguration(IConfiguration configuration)
    {
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
            foreach (var child in children.OrderBy(child => int.Parse(child.Key)))
            {
                array.Add(ReadConfiguration(child));
            }

            return array;
        }

        var obj = new JsonObject();
        foreach (var child in children)
        {
            obj[child.Key] = ReadConfiguration(child);
        }

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
            if (!int.TryParse(child.Key, out var index))
                return false;

            indexes.Add(index);
        }

        indexes.Sort();
        for (var i = 0; i < indexes.Count; i++)
        {
            if (indexes[i] != i)
                return false;
        }

        return true;
    }
}
