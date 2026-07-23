using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Decodes a Base64 string <see cref="FlowValue"/> into binary content.</summary>
public sealed class Base64DecodeNode : IFlowNode
{
    public const string NodeType = "base64.decode";

    private readonly SerializationPipeline<FlowValue, FlowContent> _pipeline;

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
            static settings => value =>
                SerializationConverters.DecodeBase64(value, settings),
            clock);

    public ITargetBlock<FlowMessage<FlowValue>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowContent>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
