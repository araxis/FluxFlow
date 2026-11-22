using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping;
using FluxFlow.Core.Messaging;

await using var mapper = new Mapper(new MapperOptions
{
    MapAsync = (value, _) => ValueTask.FromResult<FlowValue>(
        new JsonFlowValue(JsonSerializer.SerializeToElement(value.Value.Json.GetInt32() * 2))),
    Execution = new() { BoundedCapacity = 1 },
    OutputCapacity = 1
});
var sink = new BufferBlock<FlowMessage>();
using var link = mapper.Output.LinkTo(sink, new() { PropagateCompletion = true });
var request = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(21)));
await mapper.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
await mapper.Input.SendAsync(request);
mapper.Complete();
var failure = await sink.ReceiveAsync(TimeSpan.FromSeconds(10));
var reply = await sink.ReceiveAsync(TimeSpan.FromSeconds(10));
if (!failure.IsError || reply.Value.Json.GetInt32() != 42 ||
    reply.TraceId != request.TraceId || reply.CausationId != request.MessageId)
    throw new InvalidOperationException("Mapping/recovery/lineage failed.");
await mapper.Completion.WaitAsync(TimeSpan.FromSeconds(10));
await sink.Completion.WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"MAPPING_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
