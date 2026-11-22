using FluxFlow.Application.Addressing;
using FluxFlow.Application.Execution;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Core.Nodes;
using FluxFlow.Components.Routing;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var services = new ServiceCollection();
Gate? gate = null;
services.AddComponentFactory(ComponentAddress.Parse("main.gate"), _ => gate = new Gate()).AddInputPort<Gate>(ComponentAddress.Parse("main.gate"), "Input", node => node.Input).AddInputPort<Gate>(ComponentAddress.Parse("main.gate"), "Open", node => node.Open).AddInputPort<Gate>(ComponentAddress.Parse("main.gate"), "Close", node => node.Close).AddOutputPort<Gate>(ComponentAddress.Parse("main.gate"), "Output", node => node.Output).AddEventPort<Gate>(ComponentAddress.Parse("main.gate"), node => node.Events);
var capture = Capture.Register(services, "main.gate.Output");
await using var provider = services.BuildServiceProvider();
var app = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
await using var appExecution = app;
await appExecution.StartAsync(deadline.Token);
var message = FlowMessage.Create(JsonFlowValue.Null);
foreach (var open in new[]
{
    true,
    false,
    true
}

)
{
    var command = open ? "Open" : "Close";
    if (!await (open ? gate!.Open : gate!.Close).SendAsync(message, deadline.Token))
        throw new InvalidOperationException("Command rejected.");
    while (gate!.IsOpen != open)
        await Task.Delay(1, deadline.Token);
    if (open)
    {
        if (!await gate!.Input.SendAsync(message, deadline.Token))
            throw new Exception("Input rejected.");
        var reply = await capture.Messages.ReceiveAsync(deadline.Token);
        if (reply.IsError || !ReferenceEquals(reply.Value, message.Value))
            throw new InvalidOperationException("Gate changed the payload.");
    }
}

await appExecution.StopAsync(deadline.Token);
if (!gate!.Open.Completion.IsCompletedSuccessfully || !gate.Close.Completion.IsCompletedSuccessfully)
    throw new InvalidOperationException("Command inputs did not drain.");
Console.WriteLine($"ROUTING_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
sealed class Capture : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    public Capture()
    {
        var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputCapacityValue = 128;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping, handlesErrors: true), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public BufferBlock<FlowMessage> Messages { get; } = new();

    public static Capture Register(IServiceCollection services, string output)
    {
        var address = "capture" + Guid.NewGuid().ToString("N") + ".sink";
        var node = new Capture();
        services.AddComponentFactory(ComponentAddress.Parse(address), _ => node).AddInputPort<Capture>(ComponentAddress.Parse(address), "Input", sink => sink.Input);
        services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse(output), PortAddress.Parse(address + ".Input")));
        return node;
    }

    private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        Messages.Post(message);
        return Task.FromResult<IEnumerable<FlowMessage>>([]);
    }
}
