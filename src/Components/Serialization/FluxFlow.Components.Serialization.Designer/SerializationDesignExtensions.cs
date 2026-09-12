using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Serialization.Composition;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Serialization.Designer;

public static class SerializationDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddSerializationDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(SerializationComponentDefinition.Types.JsonParse, static metadata =>
                Describe(metadata, "JSON Parse", "Parses JSON content into an independently owned JSON value.", "braces", "parse"))
            .AddDesignMetadata(SerializationComponentDefinition.Types.JsonStringify, static metadata =>
                Describe(metadata, "JSON Stringify", "Serializes a JSON value into exact JSON content.", "file-json", "stringify"))
            .AddDesignMetadata(SerializationComponentDefinition.Types.TextEncode, static metadata =>
                Describe(metadata, "Text Encode", "Encodes a string into exact text content.", "binary", "encode"))
            .AddDesignMetadata(SerializationComponentDefinition.Types.TextDecode, static metadata =>
                Describe(metadata, "Text Decode", "Decodes text content into a string.", "letter-text", "decode"))
            .AddDesignMetadata(SerializationComponentDefinition.Types.Base64Encode, static metadata =>
                Describe(metadata, "Base64 Encode", "Encodes exact content bytes into a Base64 string value.", "file-up", "base64Encode"))
            .AddDesignMetadata(SerializationComponentDefinition.Types.Base64Decode, static metadata =>
                Describe(metadata, "Base64 Decode", "Decodes a Base64 string value into binary content.", "file-down", "base64Decode"));
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata,
        string displayName, string summary, string icon, string preferredName)
    {
        var defaults = new SerializationNodeOptions();
        var options = new Dictionary<string, (string Label, object Default, string Section, string? Editor, double? Min, string Help)>(StringComparer.Ordinal)
        {
            [SerializationComponentDefinition.Options.BoundedCapacity] = ("Bounded Capacity", defaults.BoundedCapacity, "Runtime", "number", 1, "Capacity used for bounded processing and reliable normal-data output."),
            [SerializationComponentDefinition.Options.DefaultEncoding] = ("Default Encoding", defaults.DefaultEncoding, "Encoding", "text", null, "Encoding used when content does not declare one."),
            [SerializationComponentDefinition.Options.MaxInputBytes] = ("Max Input Bytes", defaults.MaxInputBytes, "Runtime", "number", 1, "Maximum input payload size accepted by the node."),
            [SerializationComponentDefinition.Options.MaxOutputBytes] = ("Max Output Bytes", defaults.MaxOutputBytes, "Runtime", "number", 1, "Maximum output payload size emitted by the node."),
            [SerializationComponentDefinition.Options.WriteIndented] = ("Write Indented", defaults.WriteIndented, "JSON", null, null, "Write formatted JSON where the node emits JSON text."),
            [SerializationComponentDefinition.Options.AllowTrailingCommas] = ("Allow Trailing Commas", defaults.AllowTrailingCommas, "JSON", null, null, "Allow trailing commas while parsing JSON."),
            [SerializationComponentDefinition.Options.SkipComments] = ("Skip Comments", defaults.SkipComments, "JSON", null, null, "Skip comments while parsing JSON.")
        };
        var ports = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [SerializationComponentDefinition.Ports.Input] = ("Messages", 0, "Canonical conversion input.", true),
            [SerializationComponentDefinition.Ports.Output] = ("Results", 1, "Converted value; conversion failures use the message error case.", true),
            [SerializationComponentDefinition.Ports.Events] = ("Diagnostics", 2, "Best-effort serialization diagnostics.", false)
        };
        return metadata with
        {
            DisplayName = new ComponentMetadataText(displayName),
            Category = new ComponentCategory("Serialization"),
            Summary = new ComponentMetadataText(summary),
            IconKey = new ComponentIconKey(icon),
            PreferredNodeName = new ComponentPreferredNodeName(preferredName),
            SuggestedEditorWidth = 420,
            Options = metadata.Options.Select(option => options.TryGetValue(option.Name.Value, out var hint)
                ? option with
                {
                    DisplayName = new ComponentMetadataText(hint.Label),
                    HelperText = new ComponentMetadataText(hint.Help),
                    DefaultValue = hint.Default,
                    Min = hint.Min,
                    Attributes = OptionAttributes(hint.Section, hint.Editor)
                } : option).ToArray(),
            Ports = metadata.Ports.Select(port => ports.TryGetValue(port.Name.Value, out var hint)
                ? port with
                {
                    Group = new ComponentPortGroup(hint.Group),
                    Order = hint.Order,
                    Summary = new ComponentMetadataText(hint.Summary),
                    IsPrimary = hint.Primary
                } : port).ToArray(),
            Resources = metadata.Resources.Select(resource => resource.Name.Value == SerializationComponentDefinition.Resources.Clock
                ? resource with
                {
                    DisplayName = new ComponentMetadataText("Clock"),
                    Order = 0,
                    Summary = new ComponentMetadataText("Optional keyed clock for deterministic serialization diagnostics."),
                    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
                    {
                        [new(ResourceDesignMetadataAttributeNames.Ownership)] = new(ResourceDesignMetadataAttributeValues.HostOwned),
                        [new(ResourceDesignMetadataAttributeNames.PickerKind)] = new(ResourceDesignMetadataAttributeValues.Clock),
                        [new(ResourceDesignMetadataAttributeNames.KeyPattern)] = new("Resources.{name}")
                    }
                } : resource).ToArray()
        };
    }

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributes(string section, string? editor)
    {
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(OptionDesignMetadataAttributeNames.Section)] = new(section),
            [new(OptionDesignMetadataAttributeNames.Importance)] = new(OptionDesignMetadataAttributeValues.Advanced)
        };
        if (editor is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Editor), new(editor));
        return attributes;
    }
}
