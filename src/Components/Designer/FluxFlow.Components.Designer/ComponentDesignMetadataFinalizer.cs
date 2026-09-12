using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;

namespace FluxFlow.Components.Designer;

internal static class ComponentDesignMetadataFinalizer
{
    internal static ComponentDesignMetadata FromDescriptor(ComponentDescriptor descriptor)
        => ComponentDesignMetadataSnapshot.Create(WithCanonicalOptions(new ComponentDesignMetadata
        {
            Type = new ComponentType(descriptor.Type),
            ProcessingCapabilities = descriptor.ProcessingCapabilities,
            Options = descriptor.Options.Values.Select(CreateOption).ToArray(),
            Resources = descriptor.Resources.Values.Select(CreateResource).ToArray(),
            Ports = descriptor.Inputs.Values.Select((port, order) => CreatePort(port, PortDirection.Input, order))
                .Concat(descriptor.Outputs.Values.Select((port, order) => CreatePort(port, PortDirection.Output, order)))
                .ToArray()
        }));

    internal static OptionDesignMetadata CreateOption(ComponentOptionMetadata option)
        => new()
        {
            Name = new ComponentOptionName(option.Name),
            Kind = InferOptionKind(option.ValueType),
            IsRequired = option.IsRequired
        };

    internal static ResourceDesignMetadata CreateResource(ComponentResourceMetadata resource, int order)
        => new()
        {
            Name = new ComponentResourceName(resource.Name),
            Order = order,
            ValueType = new ComponentValueTypeHint(resource.ValueTypeHint ?? ToValueTypeHint(resource.ServiceType)),
            IsRequired = resource.IsRequired
        };

    internal static PortDesignMetadata CreatePort(ComponentPortMetadata port, PortDirection direction, int order)
        => new()
        {
            Name = new ComponentPortName(port.Name),
            Direction = direction,
            DisplayName = new ComponentMetadataText(port.Name),
            Order = order,
            ValueType = new ComponentValueTypeHint(ToValueTypeHint(port.MessageType)),
            MessageType = port.MessageType,
            Kind = port.Kind,
            LinkCardinality = port.LinkCardinality,
            InputShape = port.InputShape,
            Attributes = port.Kind == ComponentPortKind.Signal
                ? PortDesignMetadataAttributes.CreateSignalMap()
                : new Dictionary<ComponentAttributeName, ComponentAttributeValue>()
        };

    private static OptionValueKind InferOptionKind(Type valueType)
    {
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (type == typeof(bool)) return OptionValueKind.Boolean;
        if (type.IsEnum) return OptionValueKind.Enum;
        if (type == typeof(TimeSpan)) return OptionValueKind.Duration;
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) ||
            type == typeof(long) || type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return OptionValueKind.Number;
        return type.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true
            ? OptionValueKind.Json : OptionValueKind.Text;
    }

    private static string ToValueTypeHint(Type type)
    {
        if (type.IsArray) return $"{ToValueTypeHint(type.GetElementType()!)}[]";
        if (!type.IsGenericType) return type.Name;
        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = tick < 0 ? type.Name : type.Name[..tick];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(ToValueTypeHint))}>";
    }

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
        var canonicalMetadata = WithCanonicalOptions(metadata);
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

}
