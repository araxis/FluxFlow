using FluxFlow.Engine.Definitions;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Engine;

public sealed class FlowApplicationConfigurationLoader
{
    public const string DefaultSectionName = "FluxFlow:Application";

    public ApplicationDefinition Load(IConfiguration configuration, string sectionName = DefaultSectionName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new FlowApplicationConfigurationException($"Configuration section '{sectionName}' was not found.");
        }

        var json = ToJsonNode(section);
        if (json is null)
        {
            throw new FlowApplicationConfigurationException($"Configuration section '{sectionName}' is empty.");
        }

        try
        {
            var definition = json.Deserialize<ApplicationDefinition>(ApplicationDefinitionJson.CreateSerializerOptions())
                ?? throw new FlowApplicationConfigurationException($"Configuration section '{sectionName}' did not contain a flow application definition.");
            return definition with
            {
                Resources = definition.Resources ?? [],
                Workflows = definition.Workflows ?? []
            };
        }
        catch (JsonException exception)
        {
            throw new FlowApplicationConfigurationException($"Configuration section '{sectionName}' is not a valid flow application definition.", exception);
        }
    }

    private static JsonNode? ToJsonNode(IConfigurationSection section)
    {
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return section.Value is null ? null : ToScalarNode(section.Value);
        }

        if (children.All(IsArrayItem))
        {
            var array = new JsonArray();
            foreach (var child in children.OrderBy(child => int.Parse(child.Key, CultureInfo.InvariantCulture)))
            {
                array.Add(ToJsonNode(child));
            }

            return array;
        }

        var obj = new JsonObject();
        foreach (var child in children)
        {
            obj[child.Key] = ToJsonNode(child);
        }

        return obj;
    }

    private static JsonNode ToScalarNode(string value)
    {
        if (bool.TryParse(value, out var boolean))
        {
            return JsonValue.Create(boolean);
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    private static bool IsArrayItem(IConfigurationSection section)
        => int.TryParse(section.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
