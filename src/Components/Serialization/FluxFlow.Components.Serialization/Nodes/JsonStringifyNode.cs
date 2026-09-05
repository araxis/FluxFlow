using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Serializes a read-only JSON value into exact JSON content.</summary>
public sealed class JsonStringifyNode : IFlowNode
{
    public const string NodeType = "json.stringify";

    private readonly SerializationPipeline<JsonElement, FlowContent> _pipeline;

    public JsonStringifyNode(
        SerializationNodeOptions? options = null,
        TimeProvider? clock = null)
        => _pipeline = new(
            NodeType,
            options,
            SerializationResultKinds.JsonStringified,
            SerializationResultKinds.JsonStringifyFailed,
            SerializationDiagnosticNames.JsonStringified,
            SerializationDiagnosticNames.JsonStringifyFailed,
            FlowValueMaterializers.JsonElement,
            static settings => value =>
                SerializationConverters.StringifyJson(value, settings),
            clock);

    public ITargetBlock<FlowMessage> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage> Output => _pipeline.Output;

    public ISourceBlock<FlowMessage> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
