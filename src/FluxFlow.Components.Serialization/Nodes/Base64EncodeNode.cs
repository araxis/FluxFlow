using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// The <c>base64.encode</c> node: encodes bytes or text into base64 text.
/// </summary>
public sealed class Base64EncodeNode : SerializationTransformNode<Base64EncodeRequest, Base64EncodeResult>
{
    public const string NodeType = "base64.encode";

    public Base64EncodeNode(SerializationNodeOptions? options = null, TimeProvider? clock = null)
        : base(
            NodeType,
            options ?? new SerializationNodeOptions(),
            SerializationConverters.EncodeBase64,
            SerializationErrorCodes.Base64EncodeFailed,
            SerializationDiagnosticNames.Base64Encoded,
            SerializationDiagnosticNames.Base64EncodeFailed,
            SerializationConverters.Base64EncodeInputAttributes,
            SerializationConverters.Base64EncodeOutputAttributes,
            clock)
    {
    }
}
