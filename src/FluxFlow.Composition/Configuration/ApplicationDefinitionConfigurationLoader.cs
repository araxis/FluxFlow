using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Composition;

public sealed class ApplicationDefinitionConfigurationLoader
{
    public ApplicationDefinition Load(
        IConfiguration configuration,
        string? sectionName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfiguration source = configuration;
        if (sectionName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
            var section = configuration.GetSection(sectionName);
            if (!section.Exists())
            {
                throw new CompositionConfigurationException(
                    $"Application definition section '{sectionName}' was not found.");
            }

            source = section;
        }

        try
        {
            var node = ConfigurationJsonReader.Read(source)
                ?? throw new JsonException("Application definition configuration is empty.");
            RestoreEmptyDefinitionObjects(node);
            return ApplicationDefinitionJson.Deserialize(node.ToJsonString());
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or ArgumentException)
        {
            throw new CompositionConfigurationException(
                "Application definition configuration could not be loaded.",
                exception);
        }
    }

    private static void RestoreEmptyDefinitionObjects(JsonNode node)
    {
        if (node is not JsonObject root)
            return;

        RestoreEmptyObject(root, CanonicalApplicationProperties.Resources);
        RestoreEmptyObject(root, CanonicalApplicationProperties.Workflows);

        if (root[CanonicalApplicationProperties.Resources] is JsonObject resources)
            RestoreEmptyResourceGroups(resources);

        if (root[CanonicalApplicationProperties.Workflows] is JsonObject workflows)
        {
            foreach (var workflowName in workflows.Select(item => item.Key).ToArray())
                RestoreEmptyObject(workflows, workflowName);
        }
    }

    private static void RestoreEmptyObject(JsonObject root, string propertyName)
    {
        if (root.TryGetPropertyValue(propertyName, out var value) && value is null)
            root[propertyName] = new JsonObject();
    }

    private static void RestoreEmptyResourceGroups(JsonObject resources)
    {
        foreach (var resourceName in resources.Select(item => item.Key).ToArray())
        {
            RestoreEmptyObject(resources, resourceName);
            if (resources[resourceName] is JsonObject resource &&
                !resource.ContainsKey(CanonicalApplicationProperties.Type))
            {
                RestoreEmptyResourceGroups(resource);
            }
        }
    }
}
