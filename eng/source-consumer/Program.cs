using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Source;
using FluxFlow.Core.Messaging;

await using var source = new Source(new() { Values = [(FlowValue)"one", (FlowValue)"two"] });
var sink = new BufferBlock<FlowMessage>();
source.Output.LinkTo(sink, new() { PropagateCompletion = true });
foreach (var expected in new[] { "one", "two" })
{
    await source.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
    if ((await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Value.Json.GetString() != expected)
        throw new Exception("Unexpected sample value.");
}
source.Complete();
await Task.WhenAll(source.Completion, sink.Completion).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"SOURCE_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
