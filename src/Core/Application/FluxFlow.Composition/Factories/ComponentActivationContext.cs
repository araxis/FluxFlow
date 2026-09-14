using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Json.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public sealed class ComponentActivationContext
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions = CreateSerializerOptions();

    private readonly JsonSerializerOptions _serializerOptions;
    private CompositionProcessingSettings? _processingSettings;
    private bool _processingConfigured;

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
        _serializerOptions = serializerOptions ?? DefaultSerializerOptions;
    }

    public IServiceProvider Services { get; }

    public string WorkflowName { get; }

    public string ComponentName { get; }

    public ComponentDefinition Component { get; }

    private readonly List<(string Name, ITargetBlock<FlowMessage> Block, bool Signal, ComponentPortLinkCardinality Links)> _inputs = [];
    private readonly List<(string Name, ISourceBlock<FlowMessage> Block, bool Events, ComponentPortLinkCardinality Links)> _outputs = [];

    public void HasInput(ITargetBlock<FlowMessage> input) => HasInput(FlowPorts.InputName, input);
    public void HasInput(string name, ITargetBlock<FlowMessage> input, bool signal = false,
        ComponentPortLinkCardinality links = ComponentPortLinkCardinality.Multiple)
        => _inputs.Add((name, input, signal, links));
    public void HasOutput(ISourceBlock<FlowMessage> output) => HasOutput(FlowPorts.OutputName, output);
    public void HasOutput(string name, ISourceBlock<FlowMessage> output,
        ComponentPortLinkCardinality links = ComponentPortLinkCardinality.Multiple)
        => _outputs.Add((name, output, false, links));
    public void HasEvents(ISourceBlock<FlowMessage> events) => HasEvents(FlowPorts.EventsName, events);
    public void HasEvents(string name, ISourceBlock<FlowMessage> events)
        => _outputs.Add((name, events, true, ComponentPortLinkCardinality.Multiple));

    private Task? _completion;
    private Func<ValueTask>? _disposeAsync;

    /// <summary>Supplies additional lifetime ownership for the node returned by this factory.</summary>
    public void SetLifetime(Task? completion = null, Func<ValueTask>? disposeAsync = null)
    {
        if (_completion is not null || _disposeAsync is not null)
            throw new InvalidOperationException("Component lifetime is already configured.");
        _completion = completion;
        _disposeAsync = disposeAsync;
    }

    internal async ValueTask<ComponentInstance> BindAsync(IFlowNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (FluxFlow.Core.FlowTopology.HasOwner(node))
            throw new InvalidOperationException("The same component instance cannot be added twice or shared by graphs.");
        ComponentInstance instance;
        try
        {
            if (!_processingConfigured) ConfigureProcessing(CompositionProcessingCapabilities.Sequential);
            // Validate after the factory returns, so failed declarations still release its node.
            foreach (var name in _inputs.Select(p => p.Name).Concat(_outputs.Select(p => p.Name)))
                if (FluxFlow.Json.Links.ApplicationOutputPolicies.IsPolicy(name?.Trim() ?? ""))
                    throw new ArgumentException($"Port name '{name}' is reserved for an output policy.");
            var inputs = _inputs.ToDictionary(p => p.Name.Trim(),
                p => p.Signal ? ComponentPortMetadata.CreateSignal(p.Name, p.Links) : ComponentPortMetadata.Create(p.Name, p.Links), StringComparer.Ordinal);
            var outputs = _outputs.ToDictionary(p => p.Name.Trim(),
                p => ComponentPortMetadata.Create(p.Name, p.Links), StringComparer.Ordinal);
            instance = new ComponentInstance(node,
                _inputs.Select(p => new ComponentInputPort(p.Name, p.Block, p.Signal ? ComponentPortKind.Signal : ComponentPortKind.Message)),
                _outputs.Where(p => !p.Events).Select(p => ComponentPorts.Output(p.Name, p.Block)),
                completion: _completion, disposeAsync: _disposeAsync,
                addressableEvents: _outputs.Where(p => p.Events).Select(p => ComponentPorts.Events(p.Name, p.Block)))
            {
                InputMetadata = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ComponentPortMetadata>(inputs),
                OutputMetadata = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ComponentPortMetadata>(outputs)
            };
        }
        catch (Exception error)
        {
            try { await new ComponentInstance(node, completion: _completion, disposeAsync: _disposeAsync).DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Port registration and cleanup failed.", error, cleanup); }
            throw;
        }
        return await ComponentInstance.InitializeAsync(instance, this).ConfigureAwait(false);
    }

    public T BindConfiguration<T>(CompositionProcessingCapabilities? processingCapabilities = null)
    {
        if (!_processingConfigured || processingCapabilities is not null)
            ConfigureProcessing(processingCapabilities ?? CompositionProcessingCapabilities.Sequential);
        var properties = new Dictionary<string, JsonElement>(Component.Properties, StringComparer.Ordinal);
        CanonicalApplicationProperties.RemoveIgnoreCase(
            properties,
            CanonicalApplicationProperties.Processing);
        CanonicalApplicationProperties.RemoveIgnoreCase(properties, CanonicalApplicationProperties.DiscardUnmatched);
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
            return (TResource)(ResolveResource(typeof(TResource), key) ?? throw new InvalidOperationException("Resource is not registered."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException(
                $"Component '{WorkflowName}.{ComponentName}' resource '{name}' references '{key}' (resource path '{(ApplicationAddress.TryResolveResource(key, out var address) ? address!.Value : "invalid")}'), but no keyed service of type '{typeof(TResource).Name}' is registered.",
                exception);
        }
    }

    public TResource? GetResource<TResource>(string resourceName)
        where TResource : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var name = resourceName.Trim();
        if (!Component.Properties.TryGetValue(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidOperationException(
                $"Component '{WorkflowName}.{ComponentName}' resource '{name}' requires a non-empty key.");

        return (TResource?)ResolveResource(typeof(TResource), property.GetString()!.Trim());
    }

    private object? ResolveResource(Type type, string key)
    {
        foreach (var address in ApplicationAddress.ResolveResourcePaths(key, WorkflowName))
            if (Services.GetKeyedService(type, address.Value) is { } value) return value;
        // Preserve explicit host-owned keys after canonical document lookup.
        return Services.GetKeyedService(type, key);
    }

    internal void ConfigureProcessing(CompositionProcessingCapabilities capabilities)
    {
        if (!Enum.IsDefined(capabilities)) throw new ArgumentOutOfRangeException(nameof(capabilities));
        _processingConfigured = true;
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
        var profile = (CompositionProcessingProfile?)ResolveResource(typeof(CompositionProcessingProfile), key)
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
