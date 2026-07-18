using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Parses <see cref="FlowContent"/> into an immutable JSON <see cref="FlowValue"/>.</summary>
public sealed class FlowContentJsonParseNode : FlowSerializationNode<FlowContent, FlowValue>
{
    private readonly FlowContentCodecCatalog _catalog;

    public FlowContentJsonParseNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        : base(
            JsonParseNode.NodeType,
            options,
            SerializationResultKinds.JsonParsed,
            SerializationResultKinds.JsonParseFailed,
            SerializationDiagnosticNames.JsonParsed,
            SerializationDiagnosticNames.JsonParseFailed,
            clock)
    {
        _catalog = FlowSerializationConverters.CreateJsonCatalog(Options);
    }

    private protected override FlowValue Convert(FlowContent input)
        => FlowSerializationConverters.ParseJson(input, Options, _catalog);
}
