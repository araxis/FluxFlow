using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Composition;

public sealed class SerializationComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly SerializationNodeOptions Defaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateMetadata(
                SerializationCompositionNodeTypes.JsonParse,
                "JSON Parse",
                "Parses JSON content into an immutable workflow value.",
                "braces",
                "parse",
                nameof(FlowContent),
                "FlowResult<FlowValue>"),
            CreateMetadata(
                SerializationCompositionNodeTypes.JsonStringify,
                "JSON Stringify",
                "Serializes an immutable workflow value into JSON content.",
                "file-json",
                "stringify",
                nameof(FlowValue),
                "FlowResult<FlowContent>"),
            CreateMetadata(
                SerializationCompositionNodeTypes.TextEncode,
                "Text Encode",
                "Encodes a string workflow value into text content.",
                "binary",
                "encode",
                nameof(FlowValue),
                "FlowResult<FlowContent>"),
            CreateMetadata(
                SerializationCompositionNodeTypes.TextDecode,
                "Text Decode",
                "Decodes text content into a string workflow value.",
                "letter-text",
                "decode",
                nameof(FlowContent),
                "FlowResult<FlowValue>"),
            CreateMetadata(
                SerializationCompositionNodeTypes.Base64Encode,
                "Base64 Encode",
                "Encodes exact content bytes into a Base64 string value.",
                "file-up",
                "base64Encode",
                nameof(FlowContent),
                "FlowResult<FlowValue>"),
            CreateMetadata(
                SerializationCompositionNodeTypes.Base64Decode,
                "Base64 Decode",
                "Decodes a Base64 string value into binary content.",
                "file-down",
                "base64Decode",
                nameof(FlowValue),
                "FlowResult<FlowContent>")
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
                SerializationCompositionResourceNames.Clock,
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
                SerializationCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Canonical conversion input.",
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                SerializationCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Converted value or expected conversion error.",
                valueType: outputType,
                isPrimary: true);
    }

    private static void AddSharedOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: Defaults.BoundedCapacity,
                min: 1,
                attributes: OptionAttributes(
                    "Runtime",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "defaultEncoding",
                OptionValueKind.Text,
                displayName: "Default Encoding",
                helperText: "Encoding used when content does not declare one.",
                defaultValue: Defaults.DefaultEncoding,
                attributes: OptionAttributes(
                    "Encoding",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "maxInputBytes",
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
                "maxOutputBytes",
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
                "writeIndented",
                OptionValueKind.Boolean,
                displayName: "Write Indented",
                helperText: "Write formatted JSON where the node emits JSON text.",
                defaultValue: Defaults.WriteIndented,
                attributes: OptionAttributes(
                    "JSON",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "allowTrailingCommas",
                OptionValueKind.Boolean,
                displayName: "Allow Trailing Commas",
                helperText: "Allow trailing commas while parsing JSON.",
                defaultValue: Defaults.AllowTrailingCommas,
                attributes: OptionAttributes(
                    "JSON",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "skipComments",
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
}
