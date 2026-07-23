using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Encodes a string <see cref="FlowValue"/> into text content.</summary>
public sealed class TextEncodeNode : IFlowNode
{
    public const string NodeType = "text.encode";

    private readonly SerializationPipeline<FlowValue, FlowContent> _pipeline;

    public TextEncodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        => _pipeline = new(
            NodeType,
            options,
            SerializationResultKinds.TextEncoded,
            SerializationResultKinds.TextEncodeFailed,
            SerializationDiagnosticNames.TextEncoded,
            SerializationDiagnosticNames.TextEncodeFailed,
            static settings => value =>
                SerializationConverters.EncodeText(value, settings),
            clock);

    public ITargetBlock<FlowMessage<FlowValue>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowContent>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
