using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Serialization;

public sealed class SerializationComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(SerializationComponentTypes.JsonParse, "JSON Parse", "jsonParse", "Parses text or bytes into a JSON value.", "JsonParseRequest", "JsonParseResult"),
        Metadata(SerializationComponentTypes.JsonStringify, "JSON Stringify", "jsonStringify", "Serializes a value into JSON text and bytes.", "JsonStringifyRequest", "JsonStringifyResult"),
        Metadata(SerializationComponentTypes.TextEncode, "Text Encode", "textEncode", "Encodes text into bytes.", "TextEncodeRequest", "TextEncodeResult"),
        Metadata(SerializationComponentTypes.TextDecode, "Text Decode", "textDecode", "Decodes bytes into text.", "TextDecodeRequest", "TextDecodeResult"),
        Metadata(SerializationComponentTypes.Base64Encode, "Base64 Encode", "base64Encode", "Encodes bytes into base64 text.", "Base64EncodeRequest", "Base64EncodeResult"),
        Metadata(SerializationComponentTypes.Base64Decode, "Base64 Decode", "base64Decode", "Decodes base64 text into bytes.", "Base64DecodeRequest", "Base64DecodeResult")
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
        string inputType,
        string outputType) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Serialization",
            Summary = summary,
            IconKey = "serialization",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 420,
            Options =
            [
                new()
                {
                    Name = "boundedCapacity",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Capacity",
                    DefaultValue = 128,
                    Min = 1
                }
            ],
            Ports =
            [
                Port(SerializationComponentPorts.Input, PortDirection.Input, inputType, true),
                Port(SerializationComponentPorts.Output, PortDirection.Output, outputType, true, 1),
                Port(SerializationComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        };

    private static PortDesignMetadata Port(string name, PortDirection direction, string valueType, bool primary, int order = 0) => new()
    {
        Name = new PortName(name),
        Direction = direction,
        ValueType = valueType,
        IsPrimary = primary,
        Order = order
    };
}
