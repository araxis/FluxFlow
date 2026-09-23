using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Reducer;
using FluxFlow.Core.Messaging;

await using var reducer = new Reducer(new()
{
    InitialState = (FlowValue)"initial",
    Reduce = (_, input) => input,
    AreEqual = (a, b) => a.Json.GetString() == b.Json.GetString()
});
var sink = new BufferBlock<FlowMessage>();
reducer.Output.LinkTo(sink, new() { PropagateCompletion = true });
foreach (var text in new[] { "initial", "changed", "changed", "final" })
    await reducer.Input.SendAsync(FlowMessage.Create((FlowValue)text));
reducer.Complete();
foreach (var expected in new[] { "changed", "final" })
    if ((await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Value.Json.GetString() != expected)
        throw new Exception("Unexpected reducer output.");
await Task.WhenAll(reducer.Completion, sink.Completion).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"REDUCER_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
