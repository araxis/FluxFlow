using System.Text.Json;
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
            var node = ConfigurationJsonReader.Read(configuration);
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

}
