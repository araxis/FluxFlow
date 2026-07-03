using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public sealed class CompositionNodeFactoryContext
{
    private readonly JsonSerializerOptions _serializerOptions;

    public CompositionNodeFactoryContext(
        IServiceProvider services,
        string workflowName,
        string nodeName,
        NodeDefinition definition,
        JsonSerializerOptions? serializerOptions = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        WorkflowName = workflowName;
        NodeName = nodeName;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _serializerOptions = serializerOptions ?? CompositionDefinitionJson.CreateSerializerOptions();
    }

    public IServiceProvider Services { get; }

    public string WorkflowName { get; }

    public string NodeName { get; }

    public NodeDefinition Definition { get; }

    public IReadOnlyDictionary<string, JsonElement> Configuration => Definition.Configuration;

    public IReadOnlyDictionary<string, string> Resources => Definition.Resources;

    public T BindConfiguration<T>()
    {
        var json = JsonSerializer.Serialize(Definition.Configuration, _serializerOptions);
        return JsonSerializer.Deserialize<T>(json, _serializerOptions)
            ?? throw new InvalidOperationException(
                $"Configuration for node '{WorkflowName}.{NodeName}' could not be bound to {typeof(T).Name}.");
    }

    public T? GetConfigurationValue<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Definition.Configuration.TryGetValue(name, out var value))
            return default;

        return value.Deserialize<T>(_serializerOptions);
    }

    public string GetRequiredResourceKey(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var name = resourceName.Trim();
        if (Resources.TryGetValue(name, out var key)
            && !string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        throw new InvalidOperationException(
            $"Node '{WorkflowName}.{NodeName}' requires resource '{name}', but no resource reference was configured.");
    }

    public TResource GetRequiredResource<TResource>(string resourceName)
        where TResource : notnull
    {
        var key = GetRequiredResourceKey(resourceName);
        var name = resourceName.Trim();
        try
        {
            return Services.GetRequiredKeyedService<TResource>(key);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Node '{WorkflowName}.{NodeName}' resource '{name}' references '{key}', but no keyed service of type '{typeof(TResource).Name}' is registered.",
                exception);
        }
    }

    public TResource? GetResource<TResource>(string resourceName)
        where TResource : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var name = resourceName.Trim();
        if (!Resources.TryGetValue(name, out var key)
            || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return Services.GetKeyedService<TResource>(key.Trim());
    }
}
