using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Decodes text content into a string <see cref="FlowValue"/>.</summary>
public sealed class FlowContentTextDecodeNode : FlowSerializationNode<FlowContent, FlowValue>
{
    private readonly FlowContentCodecCatalog _catalog;

    public FlowContentTextDecodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        : base(
            TextDecodeNode.NodeType,
            options,
            SerializationResultKinds.TextDecoded,
            SerializationResultKinds.TextDecodeFailed,
            SerializationDiagnosticNames.TextDecoded,
            SerializationDiagnosticNames.TextDecodeFailed,
            clock)
    {
        _catalog = FlowSerializationConverters.CreateTextCatalog(Options);
    }

    private protected override FlowValue Convert(FlowContent input)
        => FlowSerializationConverters.DecodeText(input, Options, _catalog);
}
