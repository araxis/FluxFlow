using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Parses JSON content into an immutable <see cref="FlowValue"/>.</summary>
public sealed class JsonParseNode : IFlowNode
{
    public const string NodeType = "json.parse";

    private readonly SerializationPipeline<FlowContent, FlowValue> _pipeline;

    public JsonParseNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        => _pipeline = new(
            NodeType,
            options,
            SerializationResultKinds.JsonParsed,
            SerializationResultKinds.JsonParseFailed,
            SerializationDiagnosticNames.JsonParsed,
            SerializationDiagnosticNames.JsonParseFailed,
            static settings =>
            {
                var catalog = SerializationConverters.CreateJsonCatalog(settings);
                return content => SerializationConverters.ParseJson(
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
