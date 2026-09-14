using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Observability;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using var sdkDiagnostics = new SdkDiagnostics();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFluxFlowTelemetry()
    .ConfigureResource(r => r.AddService("FluxFlow.CollectorAcceptance"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .UseOtlpExporter();
builder.Services.AddFluxFlowTraceStream();
builder.Services.AddHostedService<ShutdownProbe>();
var app = builder.Build();
app.MapFluxFlowTraceStream();
app.MapGet("/clients", (FlowTraceStream stream) => stream.ConnectedClients);
app.MapPost("/scenario", async () =>
{
    await using var output = new FlowOutput<FlowMessage>();
    await using var left = new LeftBranch();
    await using var right = new RightBranch();
    using var first = output.LinkTo(left.Input, new DataflowLinkOptions { PropagateCompletion = true });
    using var second = output.LinkTo(right.Input, new DataflowLinkOptions { PropagateCompletion = true });
    var leftBuffer = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 12 });
    var rightBuffer = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 12 });
    using var leftSink = left.Output.LinkTo(leftBuffer, new DataflowLinkOptions { PropagateCompletion = true });
    using var rightSink = right.Output.LinkTo(rightBuffer, new DataflowLinkOptions { PropagateCompletion = true });
    var leftResults = Receive(leftBuffer);
    var rightResults = Receive(rightBuffer);
    for (var i = 0; i < 12; i++)
        if (!await output.SendAsync(FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(i, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))))
            throw new InvalidOperationException("Fanout declined a message.");
    output.Complete();
    var results = await Task.WhenAll(leftResults, rightResults);
    await Task.WhenAll(output.Completion, left.Completion, right.Completion).WaitAsync(TimeSpan.FromSeconds(15));
    if (results[0].Count(x => x.IsError) != 1 || results[1].Any(x => x.IsError)
        || left.MaximumActive < 2 || right.MaximumActive < 2)
        throw new InvalidOperationException("Fanout, recovery or concurrent execution failed.");
    return new { traceId = Activity.Current!.TraceId.ToString(), results = 24, errors = 1,
        leftMaximumActive = left.MaximumActive, rightMaximumActive = right.MaximumActive };
});
app.MapPost("/shutdown", (HttpContext context) =>
{
    context.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; });
    return Results.Ok();
});
await app.RunAsync();

static async Task<FlowMessage[]> Receive(ISourceBlock<FlowMessage> output)
{
    var results = new FlowMessage[12];
    for (var i = 0; i < results.Length; i++) results[i] = await output.ReceiveAsync(TimeSpan.FromSeconds(15));
    return results;
}

// Deliberate test workload only: telemetry still comes from the shared boundary.
abstract class Branch(bool fail) : FlowTransformNode(new FlowNodeOptions { MaxDegreeOfParallelism = 4 })
{
    private int _active;
    private int _maximum;
    public int MaximumActive => _maximum;
    protected override async Task<FlowMessage> TransformAsync(FlowMessage message)
    {
        var active = Interlocked.Increment(ref _active);
        int previous;
        do { previous = _maximum; }
        while (active > previous && Interlocked.CompareExchange(ref _maximum, active, previous) != previous);
        try
        {
            await Task.Delay(30);
            if (fail && message.Value!.Json.Deserialize<int>() == 0)
                throw new InvalidOperationException("collector-acceptance-failure", new ArgumentException("collector-inner-detail"));
            return message;
        }
        finally { Interlocked.Decrement(ref _active); }
    }
}
sealed class LeftBranch() : Branch(true);
sealed class RightBranch() : Branch(false);

sealed class SdkDiagnostics : System.Diagnostics.Tracing.EventListener
{
    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
    {
        if (source.Name.StartsWith("OpenTelemetry")) EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose);
    }
    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs data)
        => Console.Error.WriteLine($"{data.EventSource.Name} {data.EventName}: {string.Join(" | ", data.Payload?.ToArray() ?? [])}");
}

sealed class ShutdownProbe : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await using var node = new ShutdownNode();
        var buffer = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        using var sink = node.Output.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        await node.Input.SendAsync(FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("shutdown", new JsonSerializerOptions(JsonSerializerDefaults.Web))))), cancellationToken);
        await buffer.ReceiveAsync(cancellationToken);
        node.Complete();
        await node.Completion.WaitAsync(cancellationToken);
    }
    private sealed class ShutdownNode : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message) => Task.FromResult(message);
    }
}
