using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.ChangeFilter;
using FluxFlow.Core.Messaging;

await using var filter = new ChangeFilter<string>(new()
{ KeySelector = m => m.Value.Json.GetString()!, AreEqual = StringComparer.OrdinalIgnoreCase.Equals });
var sink = new BufferBlock<FlowMessage>();
filter.Output.LinkTo(sink, new() { PropagateCompletion = true });
var inputs = new[] { "A", "a", "B", "A" }.Select(s => FlowMessage.Create((FlowValue)s)).ToArray();
foreach (var input in inputs) await filter.Input.SendAsync(input);
filter.Complete();
foreach (var index in new[] { 0, 2, 3 })
    if (!ReferenceEquals(await sink.ReceiveAsync(TimeSpan.FromSeconds(10)), inputs[index]))
        throw new Exception("Unexpected change filtering or message identity.");
await Task.WhenAll(filter.Completion, sink.Completion).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"CHANGEFILTER_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
