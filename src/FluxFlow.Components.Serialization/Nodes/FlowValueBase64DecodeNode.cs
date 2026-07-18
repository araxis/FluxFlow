using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Decodes a Base64 string <see cref="FlowValue"/> into binary content.</summary>
public sealed class FlowValueBase64DecodeNode : FlowSerializationNode<FlowValue, FlowContent>
{
    public FlowValueBase64DecodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        : base(
            Base64DecodeNode.NodeType,
            options,
            SerializationResultKinds.Base64Decoded,
            SerializationResultKinds.Base64DecodeFailed,
            SerializationDiagnosticNames.Base64Decoded,
            SerializationDiagnosticNames.Base64DecodeFailed,
            clock)
    {
    }

    private protected override FlowContent Convert(FlowValue input)
        => FlowSerializationConverters.DecodeBase64(input, Options);
}
