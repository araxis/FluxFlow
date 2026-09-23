using FluxFlow.Core.Execution;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Application;
using FluxFlow.Core.Nodes;
using FluxFlow.Components.Routing;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var services = new ServiceCollection();
Gate? gate = null;
services.AddComponentFactory("main.gate", _ => gate = new Gate())
    .AddInputPort<Gate>("main.gate", "Input", node => node.Input)
    .AddInputPort<Gate>("main.gate", "Open", node => node.Open)
    .AddInputPort<Gate>("main.gate", "Close", node => node.Close)
    .AddOutputPort<Gate>("main.gate", "Output", node => node.Output)
    .AddEventPort<Gate>("main.gate", node => node.Events);
var capture = Capture.Register(services, "main.gate.Output");
await using var provider = services.BuildServiceProvider();
var app = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
await using var appExecution = app;
await appExecution.StartAsync(deadline.Token);
var message = FlowMessage.Create(JsonFlowValue.Null);
foreach (var open in new[] { true, false, true })
{
    var command = open ? "Open" : "Close";
    if (!await (open ? gate!.Open : gate!.Close).SendAsync(message, deadline.Token))
        throw new InvalidOperationException("Command rejected.");
    while (gate!.IsOpen != open) await Task.Delay(1, deadline.Token);
    if (open)
    {
        if (!await gate!.Input.SendAsync(message, deadline.Token)) throw new Exception("Input rejected.");
        var reply = await capture.Messages.ReceiveAsync(deadline.Token);
        if (reply.IsError || !ReferenceEquals(reply.Value, message.Value))
            throw new InvalidOperationException("Gate changed the payload.");
    }
}
await appExecution.StopAsync(deadline.Token);
if (!gate!.Open.Completion.IsCompletedSuccessfully || !gate.Close.Completion.IsCompletedSuccessfully)
    throw new InvalidOperationException("Command inputs did not drain.");
Console.WriteLine($"ROUTING_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

sealed class Capture : FlowTransformNode
{
    protected override bool HandlesErrors => true;
    public BufferBlock<FlowMessage> Messages { get; } = new();
    public static Capture Register(IServiceCollection services, string output)
    {
        var address = "capture" + Guid.NewGuid().ToString("N") + ".sink";
        var node = new Capture();
        services.AddComponentFactory(address, _ => node)
            .AddInputPort<Capture>(address, "Input", sink => sink.Input);
        services.AddSingleton<LinkFactory>(ports => ports.Link(output, address + ".Input"));
        return node;
    }
    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        Messages.Post(message);
        return Task.FromResult<IEnumerable<FlowMessage>>([]);
    }
}
