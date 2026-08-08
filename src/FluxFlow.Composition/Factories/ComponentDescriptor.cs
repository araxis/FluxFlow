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
        : this(
            type,
            factory,
            factory,
            ComponentFactoryMode.Instance,
            registrationBindings: [],
            inputs,
            outputs,
            processingCapabilities,
            options,
            resources)
    {
    }

    internal ComponentDescriptor(
        string type,
        ComponentFactory factory,
        Delegate registrationFactory,
        ComponentFactoryMode factoryMode,
        IReadOnlyList<ComponentBindingIdentity> registrationBindings,
        IEnumerable<ComponentPortMetadata>? inputs = null,
        IEnumerable<ComponentPortMetadata>? outputs = null,
        CompositionProcessingCapabilities processingCapabilities =
            CompositionProcessingCapabilities.Sequential,
        IEnumerable<ComponentOptionMetadata>? options = null,
        IEnumerable<ComponentResourceMetadata>? resources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(registrationFactory);
        ArgumentNullException.ThrowIfNull(registrationBindings);

        Type = type.Trim();
        ProcessingCapabilities = processingCapabilities;
        Inputs = ToPortDictionary(inputs).ToFrozenDictionary(StringComparer.Ordinal);
        Options = ToMetadataDictionary(options, static option => option.Name, nameof(options))
            .ToFrozenDictionary(StringComparer.Ordinal);
        Resources = ToMetadataDictionary(resources, static resource => resource.Name, nameof(resources))
            .ToFrozenDictionary(StringComparer.Ordinal);
        Outputs = ToPortDictionary(outputs).ToFrozenDictionary(StringComparer.Ordinal);
        RegistrationFactory = registrationFactory;
        RegistrationFactoryMode = factoryMode;
        RegistrationBindings = registrationBindings.ToArray();
        Factory = async context =>
        {
            context.ConfigureProcessing(ProcessingCapabilities);
            var component = await factory(context).ConfigureAwait(false);
            if (component is null)
                return null!;

            try
            {
                component.AttachAddressableEvents(context.WorkflowName, context.ComponentName);
                return component;
            }
            catch (Exception activationFailure)
            {
                try
                {
                    await component.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        $"Component type '{Type}' event attachment and cleanup failed.",
                        activationFailure,
                        cleanupFailure);
                }

                throw;
            }
        };
    }

    public string Type { get; }

    public ComponentFactory Factory { get; }

    internal Delegate RegistrationFactory { get; }

    internal ComponentFactoryMode RegistrationFactoryMode { get; }

    internal IReadOnlyList<ComponentBindingIdentity> RegistrationBindings { get; }

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
