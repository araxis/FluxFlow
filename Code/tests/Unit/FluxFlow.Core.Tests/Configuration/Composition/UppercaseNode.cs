using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Composition.Tests;
internal sealed class UppercaseNode : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    public UppercaseNode()
    {
        var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputCapacityValue = 128;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task ProcessAsync(FlowMessage message) => await EmitAsync(message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<string>()!.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping).ConfigureAwait(false);
}
