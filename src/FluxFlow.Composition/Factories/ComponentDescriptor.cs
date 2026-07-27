using System.Collections.Frozen;

namespace FluxFlow.Composition;

public sealed class ComponentDescriptor
{
    public ComponentDescriptor(
        string type,
        ComponentFactory factory,
        IEnumerable<ComponentPortMetadata>? inputs = null,
        IEnumerable<ComponentPortMetadata>? outputs = null)
        : this(
            type,
            factory,
            inputs,
            outputs,
            CompositionProcessingCapabilities.Sequential)
    {
    }

    public ComponentDescriptor(
        string type,
        ComponentFactory factory,
        IEnumerable<ComponentPortMetadata>? inputs,
        IEnumerable<ComponentPortMetadata>? outputs,
        CompositionProcessingCapabilities processingCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);

        Type = type.Trim();
        ProcessingCapabilities = processingCapabilities;
        Inputs = ToPortDictionary(inputs).ToFrozenDictionary(StringComparer.Ordinal);
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
}
