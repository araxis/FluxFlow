using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Serializes an immutable <see cref="FlowValue"/> into JSON content.</summary>
public sealed class FlowValueJsonStringifyNode : FlowSerializationNode<FlowValue, FlowContent>
{
    public FlowValueJsonStringifyNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        : base(
            JsonStringifyNode.NodeType,
            options,
            SerializationResultKinds.JsonStringified,
            SerializationResultKinds.JsonStringifyFailed,
            SerializationDiagnosticNames.JsonStringified,
            SerializationDiagnosticNames.JsonStringifyFailed,
            clock)
    {
    }

    private protected override FlowContent Convert(FlowValue input)
        => FlowSerializationConverters.StringifyJson(input, Options);
}
