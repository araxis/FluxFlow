using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignMetadataCatalog
{
    private const string ComponentEventsPortName = "Events";
    private const string ComponentEventsValueType = "CompositionComponentEvent";
    private const string ProcessingOptionName = "processing";
    private static readonly string[] CompatibilityOptionNames =
    [
        "name",
        "boundedCapacity",
        "maxDegreeOfParallelism",
        "ensureOrdered"
    ];
    private readonly Dictionary<ComponentType, ComponentDesignMetadata> _metadata = [];
    private readonly Dictionary<ComponentType, ComponentType> _aliases = [];

    public IReadOnlyCollection<ComponentDesignMetadata> All => _metadata.Values.ToArray();

    public static ComponentDesignMetadataCatalog FromProviders(IEnumerable<IComponentDesignMetadataProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var catalog = new ComponentDesignMetadataCatalog();
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var metadataItems = provider.GetMetadata()
                ?? throw new InvalidOperationException(
                    $"Design metadata provider '{provider.GetType().FullName}' returned a null metadata collection.");

            foreach (var metadata in metadataItems)
                catalog.Add(metadata);
        }

        return catalog;
    }

    public ComponentDesignMetadataCatalog Add(ComponentDesignMetadata metadata)
    {
        var canonicalMetadata = WithComponentEvents(WithCanonicalOptions(metadata));
        ComponentDesignMetadataValidator.ThrowIfInvalid(canonicalMetadata);
        var snapshot = Snapshot(canonicalMetadata);
        var aliases = ReadAliases(snapshot);

        if (_metadata.ContainsKey(snapshot.Type) || _aliases.ContainsKey(snapshot.Type))
        {
            throw new InvalidOperationException(
                $"Design metadata or alias for component type '{snapshot.Type}' is already registered.");
        }

        foreach (var alias in aliases)
        {
            if (_metadata.ContainsKey(alias) || _aliases.ContainsKey(alias))
            {
                throw new InvalidOperationException(
                    $"Design metadata or alias for component type '{alias}' is already registered.");
            }
        }

        _metadata.Add(snapshot.Type, snapshot);
        foreach (var alias in aliases)
            _aliases.Add(alias, snapshot.Type);

        return this;
    }

    private static ComponentDesignMetadata WithCanonicalOptions(ComponentDesignMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var hiddenOptions = metadata.Options
            .Where(option => CompatibilityOptionNames.Contains(
                option.Name.Value,
                StringComparer.OrdinalIgnoreCase))
            .Select(static option => option.Name.Value)
            .ToArray();
        var options = metadata.Options
            .Where(option => !hiddenOptions.Contains(option.Name.Value, StringComparer.Ordinal))
            .ToList();
        if (!options.Any(static option =>
                string.Equals(option.Name.Value, ProcessingOptionName, StringComparison.Ordinal)))
        {
            options.Add(new OptionDesignMetadata
            {
                Name = new ComponentOptionName(ProcessingOptionName),
                Kind = OptionValueKind.Text,
                DisplayName = new ComponentMetadataText("Processing"),
                HelperText = new ComponentMetadataText("Optional reusable processing profile."),
                Attributes = OptionDesignMetadataAttributes.CreateMap(
                    section: "Runtime",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text,
                    relatedResource: ProcessingOptionName)
            });
        }

        var resources = metadata.Resources.ToList();
        if (!resources.Any(static resource =>
                string.Equals(resource.Name.Value, ProcessingOptionName, StringComparison.Ordinal)))
        {
            resources.Add(new ResourceDesignMetadata
            {
                Name = new ComponentResourceName(ProcessingOptionName),
                DisplayName = new ComponentMetadataText("Processing profile"),
                Order = int.MaxValue,
                Summary = new ComponentMetadataText("Optional host-owned semantic processing profile."),
                ValueType = new ComponentValueTypeHint("CompositionProcessingProfile"),
                Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                    ResourceDesignMetadataAttributeValues.ProcessingProfile,
                    keyPattern: "Resources.{name}",
                    option: ProcessingOptionName)
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
    {
        if (_metadata.TryGetValue(type, out metadata))
            return true;

        return _aliases.TryGetValue(type, out var canonicalType) &&
               _metadata.TryGetValue(canonicalType, out metadata);
    }

    private static IReadOnlyList<ComponentType> ReadAliases(ComponentDesignMetadata metadata)
    {
        var attributeName = new ComponentAttributeName(ComponentDesignMetadataAttributeNames.Aliases);
        if (!metadata.Attributes.TryGetValue(attributeName, out var aliases))
            return [];

        var parsed = aliases.Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static alias => new ComponentType(alias))
            .Distinct()
            .ToArray();
        if (parsed.Length == 0)
        {
            throw new InvalidOperationException(
                $"Design metadata aliases for component type '{metadata.Type}' cannot be empty.");
        }
        if (parsed.Contains(metadata.Type))
        {
            throw new InvalidOperationException(
                $"Design metadata alias for component type '{metadata.Type}' must differ from its canonical type.");
        }

        return parsed;
    }

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
