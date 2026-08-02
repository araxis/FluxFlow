using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;

namespace FluxFlow.Components.Designer;

internal static class ComponentDesignMetadataFinalizer
{
    private const string ComponentEventsPortName = "Events";
    private const string ComponentEventsValueType = "ComponentEvent";
    private const string ProcessingOptionName = "processing";
    private static readonly IReadOnlyList<string> DesignerCompatibilityOptions =
    [
        "name",
        "MaxDegreeOfParallelism",
        "EnsureOrdered"
    ];

    internal static ComponentDesignMetadata Finalize(ComponentDesignMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        ComponentDesignMetadataValidator.ThrowIfInvalid(metadata);
        var canonicalMetadata = WithComponentEvents(WithCanonicalOptions(metadata));
        ComponentDesignMetadataValidator.ThrowIfInvalid(canonicalMetadata);
        return ComponentDesignMetadataSnapshot.Create(canonicalMetadata);
    }

    private static ComponentDesignMetadata WithCanonicalOptions(ComponentDesignMetadata metadata)
    {
        var hiddenOptions = metadata.Options
            .Where(option => DesignerCompatibilityOptions.Contains(
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
                    ProcessingOptionName,
                    StringComparison.Ordinal)))
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
                string.Equals(
                    resource.Name.Value,
                    ProcessingOptionName,
                    StringComparison.Ordinal)))
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
                    ValueType = new ComponentValueTypeHint(ComponentEventsValueType),
                    MessageType = typeof(ComponentEvent)
                }
            ]
        };
    }
}
