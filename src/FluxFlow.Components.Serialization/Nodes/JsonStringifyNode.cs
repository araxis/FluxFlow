using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>Serializes an immutable <see cref="FlowValue"/> into JSON content.</summary>
public sealed class JsonStringifyNode : IFlowNode
{
    public const string NodeType = "json.stringify";

    private readonly SerializationPipeline<FlowValue, FlowContent> _pipeline;

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
            static settings => value =>
                SerializationConverters.StringifyJson(value, settings),
            clock);

    public ITargetBlock<FlowMessage<FlowValue>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowContent>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();
}
