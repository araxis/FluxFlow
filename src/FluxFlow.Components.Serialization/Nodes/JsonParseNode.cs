using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// The <c>json.parse</c> node: parses text or bytes into a JSON value.
/// </summary>
public sealed class JsonParseNode : SerializationTransformNode<JsonParseRequest, JsonParseResult>
{
    public const string NodeType = "json.parse";

    public JsonParseNode(SerializationNodeOptions? options = null, TimeProvider? clock = null)
        : base(
            NodeType,
            options ?? new SerializationNodeOptions(),
            SerializationConverters.ParseJson,
            SerializationErrorCodes.JsonParseFailed,
            SerializationDiagnosticNames.JsonParsed,
            SerializationDiagnosticNames.JsonParseFailed,
            SerializationConverters.JsonParseInputAttributes,
            SerializationConverters.JsonParseOutputAttributes,
            clock)
    {
    }
}
