using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Encodes the exact bytes of <see cref="FlowContent"/> as Base64 text.</summary>
public sealed class FlowContentBase64EncodeNode : FlowSerializationNode<FlowContent, FlowValue>
{
    public FlowContentBase64EncodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        : base(
            Base64EncodeNode.NodeType,
            options,
            SerializationResultKinds.Base64Encoded,
            SerializationResultKinds.Base64EncodeFailed,
            SerializationDiagnosticNames.Base64Encoded,
            SerializationDiagnosticNames.Base64EncodeFailed,
            clock)
    {
    }

    private protected override FlowValue Convert(FlowContent input)
        => FlowSerializationConverters.EncodeBase64(input, Options);
}
