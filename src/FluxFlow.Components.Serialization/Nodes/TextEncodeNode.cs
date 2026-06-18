using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// The <c>text.encode</c> node: encodes text into bytes.
/// </summary>
public sealed class TextEncodeNode : SerializationTransformNode<TextEncodeRequest, TextEncodeResult>
{
    public const string NodeType = "text.encode";

    public TextEncodeNode(SerializationNodeOptions? options = null, TimeProvider? clock = null)
        : base(
            NodeType,
            options ?? new SerializationNodeOptions(),
            SerializationConverters.EncodeText,
            SerializationErrorCodes.TextEncodeFailed,
            SerializationDiagnosticNames.TextEncoded,
            SerializationDiagnosticNames.TextEncodeFailed,
            SerializationConverters.TextEncodeInputAttributes,
            SerializationConverters.TextEncodeOutputAttributes,
            clock)
    {
    }
}
