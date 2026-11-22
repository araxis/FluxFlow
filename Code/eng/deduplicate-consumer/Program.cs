using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Deduplicate;
using FluxFlow.Core.Messaging;

await using var filter = new Deduplicate<string>(new()
{ KeySelector = m => m.Value.Json.GetString()!, Comparer = StringComparer.OrdinalIgnoreCase,
    Retention = TimeSpan.FromMinutes(1), MaxKeys = 10 });
var sink = new BufferBlock<FlowMessage>();
filter.Output.LinkTo(sink, new() { PropagateCompletion = true });
var inputs = new[] { "A", "a", "B", "A" }.Select(s => FlowMessage.Create((FlowValue)s)).ToArray();
foreach (var input in inputs) await filter.Input.SendAsync(input);
filter.Complete();
foreach (var index in new[] { 0, 2 })
    if (!ReferenceEquals(await sink.ReceiveAsync(TimeSpan.FromSeconds(10)), inputs[index]))
        throw new Exception("Unexpected deduplication or message identity.");
await Task.WhenAll(filter.Completion, sink.Completion).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"DEDUPLICATE_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
