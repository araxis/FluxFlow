using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// The <c>text.decode</c> node: decodes bytes into text.
/// </summary>
public sealed class TextDecodeNode : SerializationTransformNode<TextDecodeRequest, TextDecodeResult>
{
    public const string NodeType = "text.decode";

    public TextDecodeNode(SerializationNodeOptions? options = null, TimeProvider? clock = null)
        : base(
            NodeType,
            options ?? new SerializationNodeOptions(),
            SerializationConverters.DecodeText,
            SerializationErrorCodes.TextDecodeFailed,
            SerializationDiagnosticNames.TextDecoded,
            SerializationDiagnosticNames.TextDecodeFailed,
            SerializationConverters.TextDecodeInputAttributes,
            SerializationConverters.TextDecodeOutputAttributes,
            clock)
    {
    }
}
