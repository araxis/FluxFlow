using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Model;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignMetadataCatalog
{
    private const string ComponentEventsPortName = "Events";
    private const string ComponentEventsValueType = "ComponentEvent";
    private readonly Dictionary<ComponentType, ComponentDesignMetadata> _metadata = [];

    public IReadOnlyCollection<ComponentDesignMetadata> All => _metadata.Values.ToArray();

    public static ComponentDesignMetadataCatalog FromProviders(
        ComponentCatalog componentCatalog,
        IEnumerable<IComponentDesignMetadataProvider> providers)
        => FromSources(componentCatalog, providers, []);

    public static ComponentDesignMetadataCatalog FromDeclarations(
        ComponentCatalog componentCatalog,
        IEnumerable<ComponentDesignDeclaration> declarations)
        => FromSources(componentCatalog, [], declarations);

    internal static ComponentDesignMetadataCatalog FromSources(
        ComponentCatalog componentCatalog,
        IEnumerable<IComponentDesignMetadataProvider> providers,
        IEnumerable<ComponentDesignDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(componentCatalog);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(declarations);

        var metadataCatalog = new ComponentDesignMetadataCatalog();
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var metadataItems = provider.GetMetadata()
                ?? throw new InvalidOperationException(
                    $"Design metadata provider '{provider.GetType().FullName}' returned a null metadata collection.");

            foreach (var metadata in metadataItems)
            {
                if (!componentCatalog.TryGetDescriptor(metadata.Type.Value, out var descriptor))
                {
                    throw new InvalidOperationException(
                        $"Design metadata type '{metadata.Type}' has no registered component descriptor.");
                }

                metadataCatalog.Add(WithDescriptor(metadata, descriptor));
            }
        }

        foreach (var declaration in declarations)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (!componentCatalog.TryGetDescriptor(
                    declaration.Descriptor.Type,
                    out var descriptor) ||
                !ReferenceEquals(descriptor, declaration.Descriptor))
            {
                throw new InvalidOperationException(
                    $"Design declaration type '{declaration.Descriptor.Type}' has no matching registered component descriptor.");
            }

            metadataCatalog.Add(WithDescriptor(declaration.Metadata, descriptor));
        }

        return metadataCatalog;
    }

    private static ComponentDesignMetadata WithDescriptor(
        ComponentDesignMetadata metadata,
        ComponentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(descriptor);

        var structuralPorts = descriptor.Inputs.Values
            .Select(static port => (Port: port, Direction: PortDirection.Input))
            .Concat(descriptor.Outputs.Values.Select(static port =>
                (Port: port, Direction: PortDirection.Output)))
            .ToDictionary(
                static item => (item.Port.Name, item.Direction),
                static item => item,
                EqualityComparer<(string, PortDirection)>.Default);
        var ports = new List<PortDesignMetadata>(structuralPorts.Count);
        var consumed = new HashSet<(string, PortDirection)>();

        foreach (var port in metadata.Ports)
        {
            var key = (port.Name.Value, port.Direction);
            if (!structuralPorts.TryGetValue(key, out var structural))
            {
                throw new InvalidOperationException(
                    $"Designer port '{metadata.Type}.{port.Name}' does not match a registered component port with direction '{port.Direction}'.");
            }

            consumed.Add(key);
            ports.Add(WithDescriptorPort(port, structural.Port, structural.Direction));
        }

        foreach (var structural in structuralPorts.Values.Where(item =>
                     !consumed.Contains((item.Port.Name, item.Direction))))
        {
            ports.Add(WithDescriptorPort(
                CreateDefaultPort(structural.Port, structural.Direction),
                structural.Port,
                structural.Direction));
        }

        return metadata with
        {
            Type = new ComponentType(descriptor.Type),
            ProcessingCapabilities = descriptor.ProcessingCapabilities,
            Options = WithDescriptorOptions(metadata, descriptor),
            Resources = WithDescriptorResources(metadata, descriptor),
            Ports = ports
        };
    }

    private static IReadOnlyList<OptionDesignMetadata> WithDescriptorOptions(
        ComponentDesignMetadata metadata,
        ComponentDescriptor descriptor)
    {
        if (descriptor.Options.Count == 0)
            return metadata.Options;

        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var options = metadata.Options.Select(option =>
        {
            if (!descriptor.Options.TryGetValue(option.Name.Value, out var structural))
            {
                throw new InvalidOperationException(
                    $"Designer option '{metadata.Type}.{option.Name}' does not match a registered component option.");
            }

            consumed.Add(option.Name.Value);
            return option with { IsRequired = structural.IsRequired };
        }).ToArray();

        var missing = descriptor.Options.Keys
            .Where(name => !consumed.Contains(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Component descriptor '{descriptor.Type}' has options without Designer metadata: {string.Join(", ", missing)}.");
        }

        return options;
    }

    private static IReadOnlyList<ResourceDesignMetadata> WithDescriptorResources(
        ComponentDesignMetadata metadata,
        ComponentDescriptor descriptor)
    {
        if (descriptor.Resources.Count == 0)
            return metadata.Resources;

        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var resources = metadata.Resources.Select(resource =>
        {
            if (!descriptor.Resources.TryGetValue(resource.Name.Value, out var structural))
            {
                throw new InvalidOperationException(
                    $"Designer resource '{metadata.Type}.{resource.Name}' does not match a registered component resource.");
            }

            consumed.Add(resource.Name.Value);
            return resource with
            {
                ValueType = new ComponentValueTypeHint(ToValueTypeHint(structural.ServiceType)),
                IsRequired = structural.IsRequired
            };
        }).ToArray();

        var missing = descriptor.Resources.Keys
            .Where(name => !consumed.Contains(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Component descriptor '{descriptor.Type}' has resources without Designer metadata: {string.Join(", ", missing)}.");
        }

        return resources;
    }

    private static PortDesignMetadata WithDescriptorPort(
        PortDesignMetadata metadata,
        ComponentPortMetadata descriptor,
        PortDirection direction)
        => metadata with
        {
            Direction = direction,
            ValueType = new ComponentValueTypeHint(ToValueTypeHint(descriptor.MessageType)),
            MessageType = descriptor.MessageType,
            Kind = descriptor.Kind,
            LinkCardinality = descriptor.LinkCardinality
        };

    private static string ToValueTypeHint(Type type)
    {
        if (type.IsArray)
            return $"{ToValueTypeHint(type.GetElementType()!)}[]";
        if (!type.IsGenericType)
            return type.Name;

        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = tick < 0 ? type.Name : type.Name[..tick];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(ToValueTypeHint))}>";
    }

    private static PortDesignMetadata CreateDefaultPort(
        ComponentPortMetadata port,
        PortDirection direction)
        => new()
        {
            Name = new ComponentPortName(port.Name),
            Direction = direction,
            DisplayName = new ComponentMetadataText(port.Name),
            Group = port.Kind == ComponentPortKind.Signal
                ? new ComponentPortGroup("Signals")
                : null,
            Order = int.MaxValue,
            Summary = port.Name == ComponentEventsPortName
                ? new ComponentMetadataText(
                    "Traced lifecycle, diagnostic, observation, warning, and metric events.")
                : null
        };

    public ComponentDesignMetadataCatalog Add(ComponentDesignMetadata metadata)
    {
        var canonicalMetadata = WithComponentEvents(WithCanonicalOptions(metadata));
        ComponentDesignMetadataValidator.ThrowIfInvalid(canonicalMetadata);
        var snapshot = Snapshot(canonicalMetadata);

        if (_metadata.ContainsKey(snapshot.Type))
        {
            throw new InvalidOperationException(
                $"Design metadata for component type '{snapshot.Type}' is already registered.");
        }

        _metadata.Add(snapshot.Type, snapshot);

        return this;
    }

    private static ComponentDesignMetadata WithCanonicalOptions(ComponentDesignMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var hiddenOptions = metadata.Options
            .Where(option => CanonicalApplicationProperties.DesignerCompatibilityOptions.Contains(
                option.Name.Value,
                StringComparer.OrdinalIgnoreCase))
            .Select(static option => option.Name.Value)
            .ToArray();
        var options = metadata.Options
            .Where(option => !hiddenOptions.Contains(option.Name.Value, StringComparer.Ordinal))
            .ToList();
        if (!options.Any(static option =>
                string.Equals(
                    option.Name.Value,
                    CanonicalApplicationProperties.DesignerProcessingOption,
                    StringComparison.Ordinal)))
        {
            options.Add(new OptionDesignMetadata
            {
                Name = new ComponentOptionName(CanonicalApplicationProperties.DesignerProcessingOption),
                Kind = OptionValueKind.Text,
                DisplayName = new ComponentMetadataText("Processing"),
                HelperText = new ComponentMetadataText("Optional reusable processing profile."),
                Attributes = OptionDesignMetadataAttributes.CreateMap(
                    section: "Runtime",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text,
                    relatedResource: CanonicalApplicationProperties.DesignerProcessingOption)
            });
        }

        var resources = metadata.Resources.ToList();
        if (!resources.Any(static resource =>
                string.Equals(
                    resource.Name.Value,
                    CanonicalApplicationProperties.DesignerProcessingOption,
                    StringComparison.Ordinal)))
        {
            resources.Add(new ResourceDesignMetadata
            {
                Name = new ComponentResourceName(CanonicalApplicationProperties.DesignerProcessingOption),
                DisplayName = new ComponentMetadataText("Processing profile"),
                Order = int.MaxValue,
                Summary = new ComponentMetadataText("Optional host-owned semantic processing profile."),
                ValueType = new ComponentValueTypeHint("CompositionProcessingProfile"),
                Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                    ResourceDesignMetadataAttributeValues.ProcessingProfile,
                    keyPattern: "Resources.{name}",
                    option: CanonicalApplicationProperties.DesignerProcessingOption)
            });
        }

        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>(
            metadata.Attributes);
        if (hiddenOptions.Length > 0)
        {
            var omittedName = new ComponentAttributeName("omittedOptions");
            var omitted = attributes.TryGetValue(omittedName, out var existing)
                ? existing.Value.Split(
                    [',', ';', ' '],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
            attributes[omittedName] = new ComponentAttributeValue(string.Join(
                ',',
                omitted.Concat(hiddenOptions).Distinct(StringComparer.Ordinal)));

            var reasonName = new ComponentAttributeName("omittedOptionsReason");
            const string reason =
                "Runtime identity and technical scheduling settings remain accepted for compatibility; canonical definitions use the component key and an optional processing profile.";
            attributes[reasonName] = attributes.TryGetValue(reasonName, out var existingReason)
                ? new ComponentAttributeValue($"{existingReason.Value} {reason}")
                : new ComponentAttributeValue(reason);
        }

        return metadata with
        {
            Options = options.ToArray(),
            Resources = resources.ToArray(),
            Attributes = attributes
        };
    }

    private static ComponentDesignMetadata WithComponentEvents(ComponentDesignMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var eventPort = metadata.Ports.SingleOrDefault(static port =>
            string.Equals(port.Name.Value, ComponentEventsPortName, StringComparison.Ordinal));
        if (eventPort is not null)
        {
            if (eventPort.Direction != PortDirection.Output ||
                !string.Equals(
                    eventPort.ValueType?.Value,
                    ComponentEventsValueType,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Designer port '{ComponentEventsPortName}' is reserved for traced component events.");
            }

            return metadata;
        }

        return metadata with
        {
            Ports =
            [
                .. metadata.Ports,
                new PortDesignMetadata
                {
                    Name = new ComponentPortName(ComponentEventsPortName),
                    Direction = PortDirection.Output,
                    DisplayName = new ComponentMetadataText("Events"),
                    Group = new ComponentPortGroup("Diagnostics"),
                    Order = int.MaxValue,
                    Summary = new ComponentMetadataText(
                        "Traced lifecycle, diagnostic, observation, warning, and metric events."),
                    ValueType = new ComponentValueTypeHint(ComponentEventsValueType)
                }
            ]
        };
    }

    public ComponentDesignMetadataCatalog AddRange(IEnumerable<ComponentDesignMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        foreach (var item in metadata)
            Add(item);

        return this;
    }

    public bool TryGet(ComponentType type, [NotNullWhen(true)] out ComponentDesignMetadata? metadata)
        => _metadata.TryGetValue(type, out metadata);

    private static ComponentDesignMetadata Snapshot(ComponentDesignMetadata metadata)
        => metadata with
        {
            Options = metadata.Options.Select(Snapshot).ToArray(),
            Resources = metadata.Resources.Select(Snapshot).ToArray(),
            Ports = metadata.Ports.Select(Snapshot).ToArray(),
            Attributes = Snapshot(metadata.Attributes)
        };

    private static OptionDesignMetadata Snapshot(OptionDesignMetadata option)
        => option with
        {
            Choices = option.Choices.Select(Snapshot).ToArray(),
            Attributes = Snapshot(option.Attributes)
        };

    private static OptionChoiceMetadata Snapshot(OptionChoiceMetadata choice)
        => choice with
        {
            Attributes = Snapshot(choice.Attributes)
        };

    private static ResourceDesignMetadata Snapshot(ResourceDesignMetadata resource)
        => resource with
        {
            Attributes = Snapshot(resource.Attributes)
        };

    private static PortDesignMetadata Snapshot(PortDesignMetadata port)
        => port with
        {
            Attributes = Snapshot(port.Attributes)
        };

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Snapshot(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes)
        => new Dictionary<ComponentAttributeName, ComponentAttributeValue>(attributes);
}
