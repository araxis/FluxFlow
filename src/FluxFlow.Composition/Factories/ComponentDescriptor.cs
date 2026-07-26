using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace FluxFlow.Composition;

public sealed class ComponentDescriptor
{
    public ComponentDescriptor(
        string type,
        ComponentFactory factory,
        IEnumerable<ComponentPortMetadata>? inputs = null,
        IEnumerable<ComponentPortMetadata>? outputs = null,
        IEnumerable<string>? aliases = null)
        : this(
            type,
            factory,
            inputs,
            outputs,
            CompositionProcessingCapabilities.Sequential,
            aliases)
    {
    }

    public ComponentDescriptor(
        string type,
        ComponentFactory factory,
        IEnumerable<ComponentPortMetadata>? inputs,
        IEnumerable<ComponentPortMetadata>? outputs,
        CompositionProcessingCapabilities processingCapabilities,
        IEnumerable<string>? aliases = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);

        Type = type.Trim();
        Aliases = ToAliases(Type, aliases);
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

    public IReadOnlyList<string> Aliases { get; }

    public ComponentFactory Factory { get; }

    public IReadOnlyDictionary<string, ComponentPortMetadata> Inputs { get; }

    public IReadOnlyDictionary<string, ComponentPortMetadata> Outputs { get; }

    public CompositionProcessingCapabilities ProcessingCapabilities { get; }

    private static IReadOnlyList<string> ToAliases(
        string type,
        IEnumerable<string>? aliases)
    {
        if (aliases is null)
            return Array.Empty<string>();

        var normalized = aliases
            .Select(static alias =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(alias);
                return alias.Trim();
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < normalized.Length; index++)
        {
            var alias = normalized[index];
            if (string.Equals(alias, type, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A component type alias must differ from its canonical type.",
                    nameof(aliases));
            }

            if (index > 0 && string.Equals(alias, normalized[index - 1], StringComparison.Ordinal))
                throw new ArgumentException($"Duplicate component type alias '{alias}'.", nameof(aliases));
        }

        return new ReadOnlyCollection<string>(normalized);
    }

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
