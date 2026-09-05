using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Decodes a Base64 string into binary content.</summary>
public sealed class Base64DecodeNode : IFlowNode
{
    public const string NodeType = "base64.decode";

    private readonly SerializationPipeline<string, FlowContent> _pipeline;

    public Base64DecodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        => _pipeline = new(
            NodeType,
            options,
            SerializationResultKinds.Base64Decoded,
            SerializationResultKinds.Base64DecodeFailed,
            SerializationDiagnosticNames.Base64Decoded,
            SerializationDiagnosticNames.Base64DecodeFailed,
            FlowValueMaterializers.String,
            static settings => value =>
                SerializationConverters.DecodeBase64(value, settings),
            clock);

    public ITargetBlock<FlowMessage> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage> Output => _pipeline.Output;

    public ISourceBlock<FlowMessage> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
