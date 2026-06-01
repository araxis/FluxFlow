using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Serialization;

public static class SerializationComponentTypes
{
    public static readonly NodeType JsonParse = new("json.parse");
    public static readonly NodeType JsonStringify = new("json.stringify");
    public static readonly NodeType TextEncode = new("text.encode");
    public static readonly NodeType TextDecode = new("text.decode");
    public static readonly NodeType Base64Encode = new("base64.encode");
    public static readonly NodeType Base64Decode = new("base64.decode");
}
