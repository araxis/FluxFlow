using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.ComponentPackageTemplate.Options;

internal static class TemplateOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static TemplateEnrichOptions ReadEnrichOptions(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var options = definition.Configuration.Count == 0
            ? new TemplateEnrichOptions()
            : JsonSerializer.Deserialize<TemplateEnrichOptions>(
                JsonSerializer.Serialize(definition.Configuration, SerializerOptions),
                SerializerOptions) ?? new TemplateEnrichOptions();

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "Template option 'boundedCapacity' must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Prefix))
        {
            throw new InvalidOperationException(
                "Template option 'prefix' is required.");
        }

        return options;
    }
}
