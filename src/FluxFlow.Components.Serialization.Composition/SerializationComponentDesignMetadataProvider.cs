using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Options;

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
                "Parses text or bytes into a JSON result.",
                "braces",
                "parse",
                nameof(JsonParseRequest),
                nameof(JsonParseResult)),
            CreateMetadata(
                SerializationCompositionNodeTypes.JsonStringify,
                "JSON Stringify",
                "Serializes a value into JSON text and bytes.",
                "file-json",
                "stringify",
                nameof(JsonStringifyRequest),
                nameof(JsonStringifyResult)),
            CreateMetadata(
                SerializationCompositionNodeTypes.TextEncode,
                "Text Encode",
                "Encodes text into bytes.",
                "binary",
                "encode",
                nameof(TextEncodeRequest),
                nameof(TextEncodeResult)),
            CreateMetadata(
                SerializationCompositionNodeTypes.TextDecode,
                "Text Decode",
                "Decodes bytes into text.",
                "letter-text",
                "decode",
                nameof(TextDecodeRequest),
                nameof(TextDecodeResult)),
            CreateMetadata(
                SerializationCompositionNodeTypes.Base64Encode,
                "Base64 Encode",
                "Encodes bytes or text into base64 text.",
                "file-up",
                "base64Encode",
                nameof(Base64EncodeRequest),
                nameof(Base64EncodeResult)),
            CreateMetadata(
                SerializationCompositionNodeTypes.Base64Decode,
                "Base64 Decode",
                "Decodes base64 text into bytes and optional text.",
                "file-down",
                "base64Decode",
                nameof(Base64DecodeRequest),
                nameof(Base64DecodeResult))
        ];

    private static ComponentDesignMetadata CreateMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        string inputType,
        string outputType) => new()
        {
            Type = new ComponentType(type),
            DisplayName = displayName,
            Category = "Serialization",
            Summary = summary,
            IconKey = iconKey,
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 420,
            Options = SharedOptions(),
            Resources = ClockResources(),
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName(SerializationCompositionPortNames.Input),
                    Direction = PortDirection.Input,
                    DisplayName = "Input",
                    Group = "Messages",
                    Order = 0,
                    Summary = "Serialization request message.",
                    ValueType = inputType,
                    IsPrimary = true
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName(SerializationCompositionPortNames.Output),
                    Direction = PortDirection.Output,
                    DisplayName = "Output",
                    Group = "Results",
                    Order = 1,
                    Summary = "Serialization result message.",
                    ValueType = outputType,
                    IsPrimary = true
                }
            ]
        };

    private static IReadOnlyList<OptionDesignMetadata> SharedOptions()
        =>
        [
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = Defaults.BoundedCapacity,
                Min = 1,
                HelperText = "Maximum queued input messages."
            },
            new OptionDesignMetadata
            {
                Name = "defaultEncoding",
                Kind = OptionValueKind.Text,
                DisplayName = "Default Encoding",
                DefaultValue = Defaults.DefaultEncoding,
                HelperText = "Encoding name used when a request does not specify one."
            },
            new OptionDesignMetadata
            {
                Name = "maxInputBytes",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Input Bytes",
                DefaultValue = Defaults.MaxInputBytes,
                Min = 1,
                HelperText = "Maximum input payload size accepted by the node."
            },
            new OptionDesignMetadata
            {
                Name = "maxOutputBytes",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Output Bytes",
                DefaultValue = Defaults.MaxOutputBytes,
                Min = 1,
                HelperText = "Maximum output payload size emitted by the node."
            },
            new OptionDesignMetadata
            {
                Name = "writeIndented",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Write Indented",
                DefaultValue = Defaults.WriteIndented,
                HelperText = "Write formatted JSON where the node emits JSON text."
            },
            new OptionDesignMetadata
            {
                Name = "allowTrailingCommas",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Allow Trailing Commas",
                DefaultValue = Defaults.AllowTrailingCommas,
                HelperText = "Allow trailing commas while parsing JSON."
            },
            new OptionDesignMetadata
            {
                Name = "skipComments",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Skip Comments",
                DefaultValue = Defaults.SkipComments,
                HelperText = "Skip comments while parsing JSON."
            }
        ];

    private static IReadOnlyList<ResourceDesignMetadata> ClockResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = SerializationCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 0,
                Summary = "Optional keyed clock for deterministic serialization diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ];
}
