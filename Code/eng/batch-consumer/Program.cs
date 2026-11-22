using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Batch;
using FluxFlow.Core.Messaging;

await using var batch = new Batch(new() { BatchSize = 3, MaxWait = TimeSpan.FromMilliseconds(100) });
var sink = new BufferBlock<FlowMessage>();
batch.Output.LinkTo(sink, new() { PropagateCompletion = true });
var originals = Enumerable.Range(0, 2).Select(i => FlowMessage.Create((FlowValue)i.ToString(), CorrelationId.New())).ToArray();
foreach (var message in originals) await batch.Input.SendAsync(message);
var timed = (FlowBatch)(await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Value;
if (timed.Items.Count != 2 || !ReferenceEquals(timed.Items[0], originals[0])) throw new Exception("Timed batch incorrect.");
await batch.Input.SendAsync(originals[0]);
batch.Complete();
var partial = (FlowBatch)(await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Value;
if (partial.Items.Count != 1 || partial.Json.GetArrayLength() != 1) throw new Exception("Final batch incorrect.");
await Task.WhenAll(batch.Completion, sink.Completion).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"BATCH_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
