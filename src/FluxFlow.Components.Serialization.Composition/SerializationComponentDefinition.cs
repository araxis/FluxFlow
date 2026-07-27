using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Composition;

public static partial class SerializationComponentDefinition
{
    private static readonly SerializationNodeOptions Defaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        =>
        [
            CreateMetadata(
                SerializationComponentDefinition.Types.JsonParse,
                "JSON Parse",
                "Parses JSON content into an independently owned JSON value.",
                "braces",
                "parse",
                nameof(FlowContent),
                nameof(JsonElement)),
            CreateMetadata(
                SerializationComponentDefinition.Types.JsonStringify,
                "JSON Stringify",
                "Serializes a JSON value into exact JSON content.",
                "file-json",
                "stringify",
                nameof(JsonElement),
                nameof(FlowContent)),
            CreateMetadata(
                SerializationComponentDefinition.Types.TextEncode,
                "Text Encode",
                "Encodes a string into exact text content.",
                "binary",
                "encode",
                nameof(String),
                nameof(FlowContent)),
            CreateMetadata(
                SerializationComponentDefinition.Types.TextDecode,
                "Text Decode",
                "Decodes text content into a string.",
                "letter-text",
                "decode",
                nameof(FlowContent),
                nameof(String)),
            CreateMetadata(
                SerializationComponentDefinition.Types.Base64Encode,
                "Base64 Encode",
                "Encodes exact content bytes into a Base64 string value.",
                "file-up",
                "base64Encode",
                nameof(FlowContent),
                nameof(String)),
            CreateMetadata(
                SerializationComponentDefinition.Types.Base64Decode,
                "Base64 Decode",
                "Decodes a Base64 string value into binary content.",
                "file-down",
                "base64Decode",
                nameof(String),
                nameof(FlowContent))
        ];

    private static ComponentDesignMetadata CreateMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        string inputType,
        string outputType)
    {
        var builder = new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Serialization",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: 420)
            .AddResource(
                SerializationComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic serialization diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"));

        AddSharedOptions(builder);
        AddSerializationPorts(builder, inputType, outputType);

        return builder.Build();
    }

    private static void AddSerializationPorts(
        ComponentDesignMetadataBuilder builder,
        string inputType,
        string outputType)
    {
        builder
            .AddInputPort(
                SerializationComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Canonical conversion input.",
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                SerializationComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Converted value; conversion failures use the message error case.",
                valueType: outputType,
                isPrimary: true);
    }

    private static void AddSharedOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(Defaults.BoundedCapacity))
            .AddOption(
                Options.DefaultEncoding,
                OptionValueKind.Text,
                displayName: "Default Encoding",
                helperText: "Encoding used when content does not declare one.",
                defaultValue: Defaults.DefaultEncoding,
                attributes: OptionAttributes(
                    "Encoding",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.MaxInputBytes,
                OptionValueKind.Number,
                displayName: "Max Input Bytes",
                helperText: "Maximum input payload size accepted by the node.",
                defaultValue: Defaults.MaxInputBytes,
                min: 1,
                attributes: OptionAttributes(
                    "Runtime",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.MaxOutputBytes,
                OptionValueKind.Number,
                displayName: "Max Output Bytes",
                helperText: "Maximum output payload size emitted by the node.",
                defaultValue: Defaults.MaxOutputBytes,
                min: 1,
                attributes: OptionAttributes(
                    "Runtime",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.WriteIndented,
                OptionValueKind.Boolean,
                displayName: "Write Indented",
                helperText: "Write formatted JSON where the node emits JSON text.",
                defaultValue: Defaults.WriteIndented,
                attributes: OptionAttributes(
                    "JSON",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.AllowTrailingCommas,
                OptionValueKind.Boolean,
                displayName: "Allow Trailing Commas",
                helperText: "Allow trailing commas while parsing JSON.",
                defaultValue: Defaults.AllowTrailingCommas,
                attributes: OptionAttributes(
                    "JSON",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.SkipComments,
                OptionValueKind.Boolean,
                displayName: "Skip Comments",
                helperText: "Skip comments while parsing JSON.",
                defaultValue: Defaults.SkipComments,
                attributes: OptionAttributes(
                    "JSON",
                    OptionDesignMetadataAttributeValues.Advanced));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);


    public static class Options
    {
        public const string BoundedCapacity = "boundedCapacity";
        public const string DefaultEncoding = "defaultEncoding";
        public const string MaxInputBytes = "maxInputBytes";
        public const string MaxOutputBytes = "maxOutputBytes";
        public const string WriteIndented = "writeIndented";
        public const string AllowTrailingCommas = "allowTrailingCommas";
        public const string SkipComments = "skipComments";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.JsonParse =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.DefaultEncoding),
                ComponentOptions.Metadata<int>(Options.MaxInputBytes),
                ComponentOptions.Metadata<int>(Options.MaxOutputBytes),
                ComponentOptions.Metadata<bool>(Options.WriteIndented),
                ComponentOptions.Metadata<bool>(Options.AllowTrailingCommas),
                ComponentOptions.Metadata<bool>(Options.SkipComments)
            ],
            Types.JsonStringify =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.DefaultEncoding),
                ComponentOptions.Metadata<int>(Options.MaxInputBytes),
                ComponentOptions.Metadata<int>(Options.MaxOutputBytes),
                ComponentOptions.Metadata<bool>(Options.WriteIndented),
                ComponentOptions.Metadata<bool>(Options.AllowTrailingCommas),
                ComponentOptions.Metadata<bool>(Options.SkipComments)
            ],
            Types.TextEncode =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.DefaultEncoding),
                ComponentOptions.Metadata<int>(Options.MaxInputBytes),
                ComponentOptions.Metadata<int>(Options.MaxOutputBytes),
                ComponentOptions.Metadata<bool>(Options.WriteIndented),
                ComponentOptions.Metadata<bool>(Options.AllowTrailingCommas),
                ComponentOptions.Metadata<bool>(Options.SkipComments)
            ],
            Types.TextDecode =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.DefaultEncoding),
                ComponentOptions.Metadata<int>(Options.MaxInputBytes),
                ComponentOptions.Metadata<int>(Options.MaxOutputBytes),
                ComponentOptions.Metadata<bool>(Options.WriteIndented),
                ComponentOptions.Metadata<bool>(Options.AllowTrailingCommas),
                ComponentOptions.Metadata<bool>(Options.SkipComments)
            ],
            Types.Base64Encode =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.DefaultEncoding),
                ComponentOptions.Metadata<int>(Options.MaxInputBytes),
                ComponentOptions.Metadata<int>(Options.MaxOutputBytes),
                ComponentOptions.Metadata<bool>(Options.WriteIndented),
                ComponentOptions.Metadata<bool>(Options.AllowTrailingCommas),
                ComponentOptions.Metadata<bool>(Options.SkipComments)
            ],
            Types.Base64Decode =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.DefaultEncoding),
                ComponentOptions.Metadata<int>(Options.MaxInputBytes),
                ComponentOptions.Metadata<int>(Options.MaxOutputBytes),
                ComponentOptions.Metadata<bool>(Options.WriteIndented),
                ComponentOptions.Metadata<bool>(Options.AllowTrailingCommas),
                ComponentOptions.Metadata<bool>(Options.SkipComments)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.JsonParse =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.JsonStringify =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.TextEncode =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.TextDecode =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Base64Encode =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Base64Decode =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string JsonParse = "json.parse";
    
        public const string JsonStringify = "json.stringify";
    
        public const string TextEncode = "text.encode";
    
        public const string TextDecode = "text.decode";
    
        public const string Base64Encode = "base64.encode";
    
        public const string Base64Decode = "base64.decode";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    }
}
