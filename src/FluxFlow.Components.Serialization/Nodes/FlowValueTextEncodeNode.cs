using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Encodes a string <see cref="FlowValue"/> into text content.</summary>
public sealed class FlowValueTextEncodeNode : FlowSerializationNode<FlowValue, FlowContent>
{
    public FlowValueTextEncodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        : base(
            TextEncodeNode.NodeType,
            options,
            SerializationResultKinds.TextEncoded,
            SerializationResultKinds.TextEncodeFailed,
            SerializationDiagnosticNames.TextEncoded,
            SerializationDiagnosticNames.TextEncodeFailed,
            clock)
    {
    }

    private protected override FlowContent Convert(FlowValue input)
        => FlowSerializationConverters.EncodeText(input, Options);
}
