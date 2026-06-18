using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// The <c>base64.decode</c> node: decodes base64 text into bytes and optional text.
/// </summary>
public sealed class Base64DecodeNode : SerializationTransformNode<Base64DecodeRequest, Base64DecodeResult>
{
    public const string NodeType = "base64.decode";

    public Base64DecodeNode(SerializationNodeOptions? options = null, TimeProvider? clock = null)
        : base(
            NodeType,
            options ?? new SerializationNodeOptions(),
            SerializationConverters.DecodeBase64,
            SerializationErrorCodes.Base64DecodeFailed,
            SerializationDiagnosticNames.Base64Decoded,
            SerializationDiagnosticNames.Base64DecodeFailed,
            SerializationConverters.Base64DecodeInputAttributes,
            SerializationConverters.Base64DecodeOutputAttributes,
            clock)
    {
    }
}
