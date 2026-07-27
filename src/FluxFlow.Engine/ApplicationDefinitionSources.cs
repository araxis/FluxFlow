using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Composition;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Engine;

public sealed class StaticApplicationDefinitionSource(
    ApplicationDefinition definition) : IApplicationDefinitionSource
{
    private readonly ApplicationDefinition _definition =
        definition ?? throw new ArgumentNullException(nameof(definition));

    public ValueTask<ApplicationDefinition> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_definition);
    }
}

public sealed class ConfigurationApplicationDefinitionSource : IApplicationDefinitionSource
{
    private readonly IConfiguration _configuration;
    private readonly string? _sectionName;

    public ConfigurationApplicationDefinitionSource(
        IConfiguration configuration,
        string? sectionName = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        if (sectionName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        _sectionName = sectionName;
    }

    public ValueTask<ApplicationDefinition> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load(_configuration, _sectionName));
    }

    private static ApplicationDefinition Load(
        IConfiguration configuration,
        string? sectionName)
    {
        IConfiguration source = configuration;
        if (sectionName is not null)
        {
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
            foreach (var workflowName in workflows.Select(static item => item.Key).ToArray())
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
        foreach (var resourceName in resources.Select(static item => item.Key).ToArray())
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
