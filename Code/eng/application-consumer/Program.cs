using FluxFlow.Application.Addressing;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Coordination;
using Microsoft.Extensions.DependencyInjection;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
await using (var requests = new FlowRequestReply())
{
    var input = FlowMessage.Create((FlowValue)new NumberValue(42));
    var reply = await requests.RequestAsync("Reply", input, (message, _) =>
    {
        if (!requests.Reply("Reply", message))
            throw new Exception("Reply did not match its request.");
        return ValueTask.CompletedTask;
    }, TimeSpan.FromSeconds(5), deadline.Token);
    if (!ReferenceEquals(reply.Value, input.Value) || reply.CorrelationId == input.CorrelationId || requests.PendingCount != 0)
        throw new Exception("Request/reply did not preserve payload identity or release its pending request.");
}

var savedValue = new NumberValue(99);
var seed = FlowMessage.Create(new NumberValue(1), new CorrelationId("lineage-check"), headers: new Dictionary<string, FlowValue> { ["saved"] = savedValue, ["source"] = "consumer" });
var nextValue = new NumberValue(2);
var generated = seed.WithValue(nextValue);
var generatedError = seed.WithValue(new FlowError("expected", "Expected failure", "consumer"));
if (generated.MessageId == seed.MessageId || generatedError.MessageId == generated.MessageId || generated.CausationId != seed.MessageId || generatedError.CausationId != seed.MessageId || generated.TraceId != seed.TraceId || generated.CorrelationId != seed.CorrelationId || !ReferenceEquals(generated.Value, nextValue) || nextValue.JsonBuilds != 0 || !generatedError.IsError || !ReferenceEquals(generated.Headers["saved"], savedValue) || savedValue.JsonBuilds != 0)
    throw new InvalidOperationException("Message generation did not preserve lineage or payload identity.");
var restoredError = JsonSerializer.Deserialize<FlowMessage>(JsonSerializer.Serialize<FlowMessage>(generatedError))!;
if (!restoredError.IsError || restoredError.MessageId != generatedError.MessageId || restoredError.CausationId != seed.MessageId)
    throw new InvalidOperationException("Error envelope did not round-trip.");
if (restoredError.Headers["saved"].Json.GetInt32() != 99 || restoredError.Headers["source"].Json.GetString() != "consumer")
    throw new InvalidOperationException("Structured headers did not round-trip.");
{
    var services = new ServiceCollection();
    var router = new NumberRouter();
    services.AddComponentFactory(ComponentAddress.Parse("routing.numbers"), _ => router).AddInputPort<NumberRouter>(ComponentAddress.Parse("routing.numbers"), "Input", node => node.Input).AddOutputPort<NumberRouter>(ComponentAddress.Parse("routing.numbers"), "Output", node => node.Output).AddOutputPort<NumberRouter>(ComponentAddress.Parse("routing.numbers"), "Negative", node => node.Negative).AddEventPort<NumberRouter>(ComponentAddress.Parse("routing.numbers"), node => node.Events);
    var positive = Capture.Register(services, "routing.numbers.Output");
    var negative = Capture.Register(services, "routing.numbers.Negative");
    await using var provider = services.BuildServiceProvider();
    var routerAddress = ComponentAddress.Parse("routing.numbers");
    if (!provider.GetComponentAddresses().Contains(routerAddress)
        || !provider.GetInputPortAddresses().Contains(routerAddress.Port("Input"))
        || !provider.GetOutputPortAddresses().Contains(routerAddress.Port("Negative"))
        || !provider.GetOutputPortAddresses().Contains(routerAddress.Port("Events")))
        throw new Exception("Discovery did not expose registered component and port addresses before composition.");
    await using var appExecution = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
    if (!ReferenceEquals(appExecution.GetComponent(ComponentAddress.Parse("routing.numbers")), router))
        throw new Exception("Application did not retain the actual component.");
    await appExecution.StartAsync(deadline.Token);
    using var liveOutput = provider.ObserveOutput(routerAddress.Port("Output"), capacity: 8);
    if (liveOutput.Address != routerAddress.Port("Output") || liveOutput.Address.ComponentAddress != routerAddress)
        throw new InvalidOperationException("Output observation lost its registered address.");
    foreach (var number in new[]
    {
        -1,
        1,
        -2,
        2
    }

    )
    {
        var payload = new NumberValue(number);
        var message = FlowMessage.Create(payload);
        var output = number < 0 ? "Negative" : "Output";
        if (!await router.Input.SendAsync(message, deadline.Token))
            throw new Exception("Input rejected.");
        var reply = await (number < 0 ? negative : positive).Messages.ReceiveAsync(deadline.Token);
        if (!ReferenceEquals(reply.Value, payload) || payload.JsonBuilds != 0)
            throw new InvalidOperationException("Routing changed or serialized the payload.");
        if (number > 0 && !ReferenceEquals(await liveOutput.Messages.ReceiveAsync(deadline.Token), reply))
            throw new InvalidOperationException("Output observation changed the message or interfered with business delivery.");
    }

    liveOutput.Dispose();
    await liveOutput.Messages.Completion.WaitAsync(deadline.Token);

    // An invalid local payload becomes error data; the next valid message still works.
    await router.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null), deadline.Token);
    var failure = await positive.Messages.ReceiveAsync(deadline.Token);
    if (!failure.IsError)
        throw new InvalidOperationException("Expected a routed processing error.");
    await router.Input.SendAsync(FlowMessage.Create(new NumberValue(3)), deadline.Token);
    var recovery = await positive.Messages.ReceiveAsync(deadline.Token);
    if (recovery.IsError || recovery.Value is not NumberValue { Number: 3 })
        throw new InvalidOperationException("Router did not recover after an error.");
    await appExecution.StopAsync(deadline.Token);
    await router.Negative.Completion.WaitAsync(deadline.Token);
}

await using (var source = new SingleValueSource())
{
    var target = new BufferBlock<FlowMessage>();
    using var link = source.Output.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
    await source.StartAsync(deadline.Token);
    var received = await target.ReceiveAsync(deadline.Token);
    if (received.Value.Json.GetInt32() != 42)
        throw new InvalidOperationException("Source payload changed.");
    await source.Completion.WaitAsync(deadline.Token);
}

{
    var services = new ServiceCollection();
    var node = new NativePassThrough();
    services.AddComponentFactory(ComponentAddress.Parse("typed.pass"), _ => node).AddInputPort<NativePassThrough>(ComponentAddress.Parse("typed.pass"), "Input", pass => pass.Block).AddOutputPort<NativePassThrough>(ComponentAddress.Parse("typed.pass"), "Output", pass => pass.Block);
    var capture = Capture.Register(services, "typed.pass.Output");
    await using var provider = services.BuildServiceProvider();
    await using var typedAppExecution = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
    await typedAppExecution.StartAsync(deadline.Token);
    var payload = new NumberValue(42);
    FlowMessage<NumberValue> message = FlowMessage.Create(payload);
    await node.Block.SendAsync(message, deadline.Token);
    var reply = await capture.Messages.ReceiveAsync(deadline.Token);
    if (!ReferenceEquals(reply.Value, payload) || payload.JsonBuilds != 0)
        throw new InvalidOperationException("Typed payload was replaced or eagerly serialized.");
    await typedAppExecution.StopAsync(deadline.Token);
}

{
    var services = new ServiceCollection();
    foreach (var address in new[]
    {
        "main.first",
        "main.second"
    }

    )
        services.AddComponentFactory(ComponentAddress.Parse(address), _ => new Increment()).AddInputPort<Increment>(ComponentAddress.Parse(address), "Input", node => node.Input).AddOutputPort<Increment>(ComponentAddress.Parse(address), "Output", node => node.Output).AddEventPort<Increment>(ComponentAddress.Parse(address), node => node.Events);
    services.AddComponentFactory(ComponentAddress.Parse("main.native"), _ => new NativePassThrough()).AddInputPort<NativePassThrough>(ComponentAddress.Parse("main.native"), "Input", node => node.Block).AddOutputPort<NativePassThrough>(ComponentAddress.Parse("main.native"), "Output", node => node.Block);
    services.AddSingleton<LinkFactory>(ports =>
    {
        var link = ports.Link(PortAddress.Parse("main.first.Output"), PortAddress.Parse("main.native.Input"));
        if (!ReferenceEquals(link.Output, ports.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.first.Output")) || link.OutputAddress != PortAddress.Parse("main.first.Output"))
            throw new Exception("Link must expose the registered native source and its address.");
        ports.Link(PortAddress.Parse("main.native.Output"), PortAddress.Parse("main.second.Input"));
    });
    var capture = Capture.Register(services, "main.second.Output");
    await using var provider = services.BuildServiceProvider();
    await using var appExecution = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
    await VerifyAsync(appExecution, capture, "main.first.Input", 2);
    await appExecution.StopAsync(deadline.Token);
}

{
    var services = new ServiceCollection();
    foreach (var address in new[]
    {
        "main.first",
        "main.second"
    }

    )
        services.AddComponentFactory(ComponentAddress.Parse(address), _ => new Increment()).AddInputPort<Increment>(ComponentAddress.Parse(address), "Input", node => node.Input).AddOutputPort<Increment>(ComponentAddress.Parse(address), "Output", node => node.Output).AddEventPort<Increment>(ComponentAddress.Parse(address), node => node.Events);
    services.AddSingleton<LinkFactory>(ports =>
    {
        var node = (Increment)ports.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.second");
        if (!ReferenceEquals(ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.second.Input"), node.Input))
            throw new Exception("Input lookup must return the native block.");
        ports.Link(PortAddress.Parse("main.first.Output"), PortAddress.Parse("main.second.Input"));
    });
    var capture = Capture.Register(services, "main.second.Output");
    await using var provider = services.BuildServiceProvider();
    var app = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
    await using var appExecution = app;
    await VerifyAsync(appExecution, capture, "main.first.Input", 2);
    await appExecution.StopAsync(deadline.Token);
}

Console.WriteLine($"APPLICATION_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
async Task VerifyAsync(FlowApplication appExecution, Capture capture, string input, int increment)
{
    var nativeInput = ((Increment)appExecution.GetComponent(ComponentAddress.Parse(input[..input.LastIndexOf('.')]))).Input;
    await appExecution.StartAsync(deadline.Token);
    await Task.WhenAll(Enumerable.Range(0, 16).Select(value => nativeInput.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value))), deadline.Token)));
    var values = new List<int>();
    for (var index = 0; index < 16; index++)
        values.Add((await capture.Messages.ReceiveAsync(deadline.Token)).Value.Json.GetInt32());
    if (!values.Order().SequenceEqual(Enumerable.Range(increment, 16)))
        throw new Exception("Delivery lost or changed values.");
    await nativeInput.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1))), deadline.Token);
    var error = await capture.Messages.ReceiveAsync(deadline.Token);
    if (!error.IsError)
        throw new InvalidOperationException("Expected a flow error.");
    await nativeInput.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3))), deadline.Token);
    var recovered = await capture.Messages.ReceiveAsync(deadline.Token);
    if (recovered.IsError || recovered.Value.Json.GetInt32() != 3 + increment)
        throw new InvalidOperationException("Processing did not continue after the error.");
}

sealed class Increment : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    public Increment()
    {
        var execution = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 32,
            MaxDegreeOfParallelism = 2
        };
        var outputCapacityValue = 2;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        => [message.WithValue(await IncrementValueAsync(message.Value, Stopping).ConfigureAwait(false))];
    private static ValueTask<FlowValue> IncrementValueAsync(FlowValue input, CancellationToken cancellationToken)
    {
        var value = input.Json.GetInt32();
        if (value < 0)
            throw new InvalidOperationException("Negative input.");
        return ValueTask.FromResult<FlowValue>(new JsonFlowValue(JsonSerializer.SerializeToElement(value + 1)));
    }
}

sealed class NativePassThrough : IFlowNode
{
    public TransformBlock<FlowMessage, FlowMessage> Block { get; } = new(message => message, new ExecutionDataflowBlockOptions { BoundedCapacity = 32 });
    public Task Completion => Block.Completion;

    public void Complete() => Block.Complete();
    public void Fault(Exception exception) => ((IDataflowBlock)Block).Fault(exception);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class NumberValue(int number) : FlowValue
{
    public int Number { get; } = number;
    public int JsonBuilds { get; private set; }

    protected override JsonElement CreateJson()
    {
        JsonBuilds++;
        return JsonSerializer.SerializeToElement(Number);
    }
}

sealed class NumberRouter : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }
    public BroadcastBlock<FlowMessage> Negative { get; }

    public NumberRouter()
    {
        var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputCapacityValue = 128;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        var lifetime = Lifetime;
        Negative = lifetime.HasOutput(new BroadcastBlock<FlowMessage>(static message => message, new DataflowBlockOptions { BoundedCapacity = 128 }));
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var value = message.Value as NumberValue ?? throw new InvalidOperationException("Expected NumberValue.");
        if (value.Number >= 0)
            return[message];
        await EmitAsync(Negative, message, Stopping);
        return[];
    }
}

sealed class SingleValueSource : FlowNode, IFlowSource
{
    public Task ProductionCompletion => Lifetime.ProductionCompletion;

    public void StopProduction() => Lifetime.StopProduction();
    public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
    public SingleValueSource()
    {
        var outputCapacityValue = 1;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, produce: RunAsync);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task RunAsync(CancellationToken cancellationToken) => await EmitAsync(FlowMessage.Create(new NumberValue(42)), cancellationToken);
}

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
