namespace FluxFlow.Composition;

public sealed class CompositionNodeRegistration
{
    public CompositionNodeRegistration(
        string type,
        CompositionNodeFactory factory,
        IEnumerable<CompositionPortMetadata>? inputs = null,
        IEnumerable<CompositionPortMetadata>? outputs = null)
        : this(
            type,
            factory,
            inputs,
            outputs,
            CompositionProcessingCapabilities.Sequential)
    {
    }

    public CompositionNodeRegistration(
        string type,
        CompositionNodeFactory factory,
        IEnumerable<CompositionPortMetadata>? inputs,
        IEnumerable<CompositionPortMetadata>? outputs,
        CompositionProcessingCapabilities processingCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        Type = type.Trim();
        ProcessingCapabilities = processingCapabilities;
        Inputs = ToPortDictionary(inputs);
        var registeredOutputs = ToPortDictionary(outputs);
        if (!registeredOutputs.TryAdd(
                CompositionComponentEvents.PortName,
                CompositionPorts.Metadata<CompositionComponentEvent>(
                    CompositionComponentEvents.PortName)))
        {
            throw new ArgumentException(
                $"Output port '{CompositionComponentEvents.PortName}' is reserved for component events.",
                nameof(outputs));
        }

        Outputs = registeredOutputs;
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

    public CompositionNodeFactory Factory { get; }

    public IReadOnlyDictionary<string, CompositionPortMetadata> Inputs { get; }

    public IReadOnlyDictionary<string, CompositionPortMetadata> Outputs { get; }

    public CompositionProcessingCapabilities ProcessingCapabilities { get; }

    private static Dictionary<string, CompositionPortMetadata> ToPortDictionary(
        IEnumerable<CompositionPortMetadata>? ports)
    {
        var result = new Dictionary<string, CompositionPortMetadata>(StringComparer.Ordinal);
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
