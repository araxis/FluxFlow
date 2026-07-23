using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Decodes text content into a string <see cref="FlowValue"/>.</summary>
public sealed class TextDecodeNode : IFlowNode
{
    public const string NodeType = "text.decode";

    private readonly SerializationPipeline<FlowContent, FlowValue> _pipeline;

    public TextDecodeNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        => _pipeline = new(
            NodeType,
            options,
            SerializationResultKinds.TextDecoded,
            SerializationResultKinds.TextDecodeFailed,
            SerializationDiagnosticNames.TextDecoded,
            SerializationDiagnosticNames.TextDecodeFailed,
            static settings =>
            {
                var catalog = SerializationConverters.CreateTextCatalog(settings);
                return content => SerializationConverters.DecodeText(
                    content,
                    settings,
                    catalog);
            },
            clock);

    public ITargetBlock<FlowMessage<FlowContent>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowValue>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
