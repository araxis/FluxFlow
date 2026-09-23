using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Resilience;
using FluxFlow.Core.Messaging;

await using var retry = new Retry(new RetryOptions
{
    RetryPolicy = new()
    {
        MaxRetryAttempts = 2, Delay = TimeSpan.Zero,
        ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result?.Error?.IsTransient == true)
    }
});
var calls = 0;
var correlations = new HashSet<CorrelationId?>();
var operation = new TransformBlock<FlowMessage, FlowMessage>(request =>
{
    if (!correlations.Add(request.CorrelationId)) throw new Exception("Exchange identity reused.");
    FlowValue value = ++calls < 3 ? new FlowError("busy", "Try again", "test", true) : (FlowValue)"done";
    return request.WithValue(value);
});
using var requestLink = retry.Request.LinkTo(operation, new() { PropagateCompletion = true });
using var replyLink = operation.LinkTo(retry.Reply, new() { PropagateCompletion = true });
var sink = new BufferBlock<FlowMessage>();
using var outputLink = retry.Output.LinkTo(sink, new() { PropagateCompletion = true });
var input = FlowMessage.Create((FlowValue)"request", CorrelationId.New());
await retry.Input.SendAsync(input);
retry.Complete();
var result = await sink.ReceiveAsync(TimeSpan.FromSeconds(10));
if (result.Value.Json.GetString() != "done" || result.CorrelationId != input.CorrelationId || calls != 3)
    throw new Exception("Retry result or outer lineage incorrect.");
await Task.WhenAll(retry.Completion, operation.Completion, sink.Completion, retry.Reply.Completion)
    .WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"RESILIENCE_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
