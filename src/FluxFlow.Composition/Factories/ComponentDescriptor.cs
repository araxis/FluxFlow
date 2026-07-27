using System.Collections.Frozen;

namespace FluxFlow.Composition;

public sealed class ComponentDescriptor
{
    public ComponentDescriptor(
        string type,
        ComponentFactory factory,
        IEnumerable<ComponentPortMetadata>? inputs = null,
        IEnumerable<ComponentPortMetadata>? outputs = null,
        CompositionProcessingCapabilities processingCapabilities =
            CompositionProcessingCapabilities.Sequential,
        IEnumerable<ComponentOptionMetadata>? options = null,
        IEnumerable<ComponentResourceMetadata>? resources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);

        Type = type.Trim();
        ProcessingCapabilities = processingCapabilities;
        Inputs = ToPortDictionary(inputs).ToFrozenDictionary(StringComparer.Ordinal);
        Options = ToMetadataDictionary(options, static option => option.Name, nameof(options))
            .ToFrozenDictionary(StringComparer.Ordinal);
        Resources = ToMetadataDictionary(resources, static resource => resource.Name, nameof(resources))
            .ToFrozenDictionary(StringComparer.Ordinal);
        var registeredOutputs = ToPortDictionary(outputs);
        if (!registeredOutputs.TryAdd(
                ComponentEvents.PortName,
                ComponentPorts.Metadata<ComponentEvent>(
                    ComponentEvents.PortName)))
        {
            throw new ArgumentException(
                $"Output port '{ComponentEvents.PortName}' is reserved for component events.",
                nameof(outputs));
        }

        Outputs = registeredOutputs.ToFrozenDictionary(StringComparer.Ordinal);
        Factory = async context =>
        {
            context.ConfigureProcessing(ProcessingCapabilities);
            var component = await factory(context).ConfigureAwait(false);
            if (component is not null)
                component.AttachAddressableEvents(context.WorkflowName, context.ComponentName);
            return component!;
        };
    }

    public string Type { get; }

    public ComponentFactory Factory { get; }

    public IReadOnlyDictionary<string, ComponentPortMetadata> Inputs { get; }

    public IReadOnlyDictionary<string, ComponentPortMetadata> Outputs { get; }

    public IReadOnlyDictionary<string, ComponentOptionMetadata> Options { get; }

    public IReadOnlyDictionary<string, ComponentResourceMetadata> Resources { get; }

    public CompositionProcessingCapabilities ProcessingCapabilities { get; }

    private static Dictionary<string, ComponentPortMetadata> ToPortDictionary(
        IEnumerable<ComponentPortMetadata>? ports)
    {
        var result = new Dictionary<string, ComponentPortMetadata>(StringComparer.Ordinal);
        if (ports is null)
            return result;

        foreach (var port in ports)
        {
            ArgumentNullException.ThrowIfNull(port);
            ArgumentException.ThrowIfNullOrWhiteSpace(port.Name);
            if (!result.TryAdd(port.Name, port))
                throw new ArgumentException($"Duplicate port name '{port.Name}'.", nameof(ports));
        }

        return result;
    }

    private static Dictionary<string, TMetadata> ToMetadataDictionary<TMetadata>(
        IEnumerable<TMetadata>? metadata,
        Func<TMetadata, string> getName,
        string parameterName)
        where TMetadata : class
    {
        var result = new Dictionary<string, TMetadata>(StringComparer.Ordinal);
        if (metadata is null)
            return result;

        foreach (var item in metadata)
        {
            ArgumentNullException.ThrowIfNull(item);
            var name = getName(item);
            if (!result.TryAdd(name, item))
                throw new ArgumentException($"Duplicate metadata name '{name}'.", parameterName);
        }

        return result;
    }
}
