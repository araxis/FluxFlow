using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public sealed class ComponentActivationContext
{
    private readonly JsonSerializerOptions _serializerOptions;
    private CompositionProcessingSettings? _processingSettings;

    public ComponentActivationContext(
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
        _serializerOptions = serializerOptions ?? CreateSerializerOptions();
    }

    public IServiceProvider Services { get; }

    public string WorkflowName { get; }

    public string ComponentName { get; }

    public ComponentDefinition Component { get; }

    public T BindConfiguration<T>()
    {
        var properties = new Dictionary<string, JsonElement>(Component.Properties, StringComparer.Ordinal);
        CanonicalApplicationProperties.RemoveIgnoreCase(
            properties,
            CanonicalApplicationProperties.Processing);
        if (!CanonicalApplicationProperties.ContainsIgnoreCase(
                properties,
                CanonicalApplicationProperties.Name))
        {
            properties.Add(
                CanonicalApplicationProperties.Name,
                JsonSerializer.SerializeToElement(ComponentName, _serializerOptions));
        }
        if (_processingSettings is not null)
        {
            CompositionProcessingConfiguration.Apply(
                properties,
                _processingSettings,
                _serializerOptions);
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
        if (!Component.Properties.TryGetValue(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return null;
        }

        return Services.GetKeyedService<TResource>(property.GetString()!.Trim());
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

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        return options;
    }

}
