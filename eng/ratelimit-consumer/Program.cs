using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.RateLimit;
using FluxFlow.Core.Messaging;

using var limiter = new ConcurrencyLimiter(new()
{ PermitLimit = 1, QueueLimit = 4, QueueProcessingOrder = QueueProcessingOrder.OldestFirst });
await using var component = new RateLimit(new() { Limiter = limiter });
var operation = new TransformBlock<FlowMessage, FlowMessage>(message => message.WithValue(message.Value));
component.Request.LinkTo(operation, new() { PropagateCompletion = true });
operation.LinkTo(component.Reply, new() { PropagateCompletion = true });
var sink = new BufferBlock<FlowMessage>();
component.Output.LinkTo(sink, new() { PropagateCompletion = true });
var originals = new[] { FlowMessage.Create((FlowValue)"one", CorrelationId.New()), FlowMessage.Create((FlowValue)"two", CorrelationId.New()) };
foreach (var original in originals) await component.Input.SendAsync(original);
component.Complete();
foreach (var original in originals)
{
    var result = await sink.ReceiveAsync(TimeSpan.FromSeconds(10));
    if (result.CorrelationId != original.CorrelationId || !ReferenceEquals(result.Value, original.Value))
        throw new Exception("Incorrect rate-limit result or lineage.");
}
await Task.WhenAll(component.Completion, operation.Completion, sink.Completion).WaitAsync(TimeSpan.FromSeconds(10));
if (limiter.GetStatistics()!.CurrentAvailablePermits != 1) throw new Exception("Leaked permit.");
Console.WriteLine($"RATELIMIT_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
