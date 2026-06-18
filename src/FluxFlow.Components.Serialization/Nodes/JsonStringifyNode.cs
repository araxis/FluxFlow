using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// The <c>json.stringify</c> node: serializes a value into JSON text and bytes.
/// </summary>
public sealed class JsonStringifyNode : SerializationTransformNode<JsonStringifyRequest, JsonStringifyResult>
{
    public const string NodeType = "json.stringify";

    public JsonStringifyNode(SerializationNodeOptions? options = null, TimeProvider? clock = null)
        : base(
            NodeType,
            options ?? new SerializationNodeOptions(),
            SerializationConverters.StringifyJson,
            SerializationErrorCodes.JsonStringifyFailed,
            SerializationDiagnosticNames.JsonStringified,
            SerializationDiagnosticNames.JsonStringifyFailed,
            SerializationConverters.JsonStringifyInputAttributes,
            SerializationConverters.JsonStringifyOutputAttributes,
            clock)
    {
    }
}
