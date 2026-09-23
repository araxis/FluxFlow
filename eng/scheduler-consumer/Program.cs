using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Scheduler;
using FluxFlow.Core.Messaging;

await using var scheduler = new Scheduler(new()
{
    NextOccurrence = Schedules.Every(TimeSpan.FromMilliseconds(50))
});
var sink = new BufferBlock<FlowMessage>();
scheduler.Output.LinkTo(sink, new() { PropagateCompletion = true });
await scheduler.StartAsync();
var tick = await sink.ReceiveAsync(TimeSpan.FromSeconds(10));
if (tick.Value.Json.ValueKind != System.Text.Json.JsonValueKind.Null) throw new Exception("Unexpected signal.");
if (typeof(Scheduler).Assembly.GetTypes().Any(type => type.Name == "SchedulerConfiguration"))
    throw new Exception("Configuration must not ship in the component assembly.");
scheduler.Complete();
while (await sink.OutputAvailableAsync()) while (sink.TryReceive(out _)) { }
await Task.WhenAll(scheduler.Completion, sink.Completion).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"SCHEDULER_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
