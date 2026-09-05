using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Encodes exact <see cref="FlowContent"/> bytes as Base64 text.</summary>
public sealed class Base64EncodeNode : IFlowNode
{
    public const string NodeType = "base64.encode";

    private readonly SerializationPipeline<FlowContent, string> _pipeline;

    public Base64EncodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        => _pipeline = new(
            NodeType,
            options,
            SerializationResultKinds.Base64Encoded,
            SerializationResultKinds.Base64EncodeFailed,
            SerializationDiagnosticNames.Base64Encoded,
            SerializationDiagnosticNames.Base64EncodeFailed,
            FlowValueMaterializers.Content,
            static settings => content =>
                SerializationConverters.EncodeBase64(content, settings),
            clock);

    public ITargetBlock<FlowMessage> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage> Output => _pipeline.Output;

    public ISourceBlock<FlowMessage> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
