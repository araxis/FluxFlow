using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public sealed class CompositionNodeFactoryContext
{
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly NodeDefinition? _legacyDefinition;
    private readonly IReadOnlyDictionary<string, string> _legacyResources;
    private CompositionProcessingSettings? _processingSettings;

    [Obsolete("Use the ComponentDefinition constructor. Legacy NodeDefinition factory contexts are planned for removal in the next major version.")]
    public CompositionNodeFactoryContext(
        IServiceProvider services,
        string workflowName,
        string nodeName,
        NodeDefinition definition,
        JsonSerializerOptions? serializerOptions = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        WorkflowName = workflowName;
        ComponentName = nodeName;
        _legacyDefinition = definition ?? throw new ArgumentNullException(nameof(definition));
        _legacyResources = definition.Resources;
        Component = ToComponentDefinition(definition);
        _serializerOptions = serializerOptions ?? CreateSerializerOptions();
    }

    public CompositionNodeFactoryContext(
        IServiceProvider services,
        string workflowName,
        string componentName,
        ComponentDefinition definition,
        JsonSerializerOptions? serializerOptions = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        WorkflowName = workflowName;
        ComponentName = componentName;
        Component = definition ?? throw new ArgumentNullException(nameof(definition));
        _legacyResources = new Dictionary<string, string>(StringComparer.Ordinal);
        _serializerOptions = serializerOptions ?? CreateSerializerOptions();
    }

    public IServiceProvider Services { get; }

    public string WorkflowName { get; }

    public string ComponentName { get; }

    [Obsolete("Use ComponentName. Node terminology is retained for compatibility.")]
    public string NodeName => ComponentName;

    public ComponentDefinition Component { get; }

    [Obsolete("Use Component. Legacy wrapped node definitions are retained for compatibility.")]
    public NodeDefinition Definition => _legacyDefinition ?? ToNodeDefinition(Component);

    public IReadOnlyDictionary<string, JsonElement> Configuration => Component.Properties;

    [Obsolete("Canonical resource references are flat component properties.")]
    public IReadOnlyDictionary<string, string> Resources => _legacyResources;

    public T BindConfiguration<T>()
    {
        var properties = new Dictionary<string, JsonElement>(Component.Properties, StringComparer.Ordinal);
        CanonicalApplicationProperties.RemoveIgnoreCase(
            properties,
            CanonicalApplicationProperties.Processing);
        if (_legacyDefinition is null &&
            !CanonicalApplicationProperties.ContainsIgnoreCase(
                properties,
                CanonicalApplicationProperties.Name))
        {
            properties.Add(
                CanonicalApplicationProperties.Name,
                JsonSerializer.SerializeToElement(ComponentName, _serializerOptions));
        }
        if (_processingSettings is not null)
        {
            AddPropertyIfMissing(properties, "BoundedCapacity", _processingSettings.BufferCapacity);
            AddPropertyIfMissing(properties, "MaxDegreeOfParallelism", _processingSettings.Concurrency);
            AddPropertyIfMissing(properties, "EnsureOrdered", _processingSettings.PreserveOrder);
        }

        var json = JsonSerializer.Serialize(properties, _serializerOptions);
        return JsonSerializer.Deserialize<T>(json, _serializerOptions)
            ?? throw new InvalidOperationException(
                $"Configuration for component '{WorkflowName}.{ComponentName}' could not be bound to {typeof(T).Name}.");
    }

    public T? GetConfigurationValue<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Component.Properties.TryGetValue(name, out var value))
            return default;

        return value.Deserialize<T>(_serializerOptions);
    }

    public string GetRequiredResourceKey(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var name = resourceName.Trim();
        if (_legacyResources.TryGetValue(name, out var key)
            && !string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        if (Component.Properties.TryGetValue(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!.Trim();
        }

        throw new InvalidOperationException(
            $"Component '{WorkflowName}.{ComponentName}' requires resource '{name}', but no resource reference was configured.");
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
                $"Component '{WorkflowName}.{ComponentName}' resource '{name}' references '{key}', but no keyed service of type '{typeof(TResource).Name}' is registered.",
                exception);
        }
    }

    public TResource? GetResource<TResource>(string resourceName)
        where TResource : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var name = resourceName.Trim();
        if (!_legacyResources.TryGetValue(name, out var key) || string.IsNullOrWhiteSpace(key))
        {
            if (!Component.Properties.TryGetValue(name, out var property) ||
                property.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.GetString()))
            {
                return null;
            }

            key = property.GetString()!;
        }

        return Services.GetKeyedService<TResource>(key.Trim());
    }

    internal void ConfigureProcessing(CompositionProcessingCapabilities capabilities)
    {
        if (!CanonicalApplicationProperties.TryGetIgnoreCase(
                Component.Properties,
                CanonicalApplicationProperties.Processing,
                out var processing))
            return;
        if (processing.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(processing.GetString()))
        {
            throw new InvalidOperationException(
                $"Component '{WorkflowName}.{ComponentName}' processing profile reference must be a non-empty string.");
        }

        var key = processing.GetString()!.Trim();
        var profile = Services.GetKeyedService<CompositionProcessingProfile>(key)
            ?? throw new InvalidOperationException(
                $"Component '{WorkflowName}.{ComponentName}' processing profile '{key}' is not registered.");
        ValidateProcessingCapabilities(profile, capabilities);
        var mapper = Services.GetService<ICompositionProcessingProfileMapper>()
            ?? new DefaultCompositionProcessingProfileMapper();
        _processingSettings = mapper.Map(profile)
            ?? throw new InvalidOperationException(
                $"Processing profile mapper returned null for component '{WorkflowName}.{ComponentName}'.");
    }

    private void ValidateProcessingCapabilities(
        CompositionProcessingProfile profile,
        CompositionProcessingCapabilities capabilities)
    {
        if (profile.Mode != CompositionProcessingMode.Parallel)
            return;

        var required = profile.Order == CompositionProcessingOrder.Preserve
            ? CompositionProcessingCapabilities.ParallelPreservingOrder
            : CompositionProcessingCapabilities.ParallelRelaxedOrder;
        if ((capabilities & required) == required)
            return;

        throw new InvalidOperationException(
            $"Component '{WorkflowName}.{ComponentName}' does not support processing mode " +
            $"'{profile.Mode}' with order '{profile.Order}'.");
    }

    private void AddPropertyIfMissing<T>(
        IDictionary<string, JsonElement> properties,
        string name,
        T value)
    {
        if (properties.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)))
            return;

        properties.Add(name, JsonSerializer.SerializeToElement(value, _serializerOptions));
    }

    private static NodeDefinition ToNodeDefinition(ComponentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new NodeDefinition
        {
            Type = definition.Type,
            Configuration = definition.Properties.ToDictionary(
                static property => property.Key,
                static property => property.Value,
                StringComparer.Ordinal)
        };
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        return options;
    }

    private static ComponentDefinition ToComponentDefinition(NodeDefinition definition)
    {
        var properties = new Dictionary<string, JsonElement>(definition.Configuration, StringComparer.Ordinal);
        foreach (var (name, key) in definition.Resources)
            properties.TryAdd(name, JsonSerializer.SerializeToElement(key));

        return new ComponentDefinition(definition.Type, properties);
    }
}
