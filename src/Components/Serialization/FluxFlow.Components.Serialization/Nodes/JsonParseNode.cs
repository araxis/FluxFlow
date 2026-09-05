using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Parses JSON content into an independently owned read-only JSON value.</summary>
public sealed class JsonParseNode : IFlowNode
{
    public const string NodeType = "json.parse";

    private readonly SerializationPipeline<FlowContent, JsonElement> _pipeline;

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
            FlowValueMaterializers.Content,
            static settings => content => SerializationConverters.ParseJson(content, settings),
            clock);

    public ITargetBlock<FlowMessage> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage> Output => _pipeline.Output;

    public ISourceBlock<FlowMessage> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
