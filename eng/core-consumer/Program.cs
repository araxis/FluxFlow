using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
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
        if (!requests.Reply("Reply", message)) throw new Exception("Reply did not match its request.");
        return ValueTask.CompletedTask;
    }, TimeSpan.FromSeconds(5), deadline.Token);
    if (!ReferenceEquals(reply.Value, input.Value) || reply.CorrelationId == input.CorrelationId || requests.PendingCount != 0)
        throw new Exception("Request/reply did not preserve payload identity or release its pending request.");
}
var savedValue = new NumberValue(99);
var seed = FlowMessage.Create(new NumberValue(1), new CorrelationId("lineage-check"),
    headers: new Dictionary<string, FlowValue> { ["saved"] = savedValue, ["source"] = "consumer" });
var nextValue = new NumberValue(2);
var generated = seed.WithValue(nextValue);
var generatedError = seed.WithValue(new FlowError("expected", "Expected failure", "consumer"));
if (generated.MessageId == seed.MessageId || generatedError.MessageId == generated.MessageId ||
    generated.CausationId != seed.MessageId || generatedError.CausationId != seed.MessageId ||
    generated.TraceId != seed.TraceId || generated.CorrelationId != seed.CorrelationId ||
    !ReferenceEquals(generated.Value, nextValue) || nextValue.JsonBuilds != 0 || !generatedError.IsError ||
    !ReferenceEquals(generated.Headers["saved"], savedValue) || savedValue.JsonBuilds != 0)
    throw new InvalidOperationException("Message generation did not preserve lineage or payload identity.");
var restoredError = JsonSerializer.Deserialize<FlowMessage>(JsonSerializer.Serialize<FlowMessage>(generatedError))!;
if (!restoredError.IsError || restoredError.MessageId != generatedError.MessageId ||
    restoredError.CausationId != seed.MessageId)
    throw new InvalidOperationException("Error envelope did not round-trip.");
if (restoredError.Headers["saved"].Json.GetInt32() != 99 ||
    restoredError.Headers["source"].Json.GetString() != "consumer")
    throw new InvalidOperationException("Structured headers did not round-trip.");
{
    var services = new ServiceCollection();
    var router = new NumberRouter();
    services.AddComponentFactory("routing.numbers", _ => router)
        .AddInputPort<NumberRouter>("routing.numbers", "Input", node => node.Input)
        .AddOutputPort<NumberRouter>("routing.numbers", "Output", node => node.Output)
        .AddOutputPort<NumberRouter>("routing.numbers", "Negative", node => node.Negative)
        .AddEventPort<NumberRouter>("routing.numbers", node => node.Events);
    var positive = Capture.Register(services, "routing.numbers.Output");
    var negative = Capture.Register(services, "routing.numbers.Negative");
    await using var provider = services.BuildServiceProvider();
    await using var appExecution = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
    if (!ReferenceEquals(appExecution.GetComponent("routing.numbers"), router))
        throw new Exception("Application did not retain the actual component.");
    await appExecution.StartAsync(deadline.Token);
    foreach (var number in new[] { -1, 1, -2, 2 })
    {
        var payload = new NumberValue(number);
        var message = FlowMessage.Create(payload);
        var output = number < 0 ? "Negative" : "Output";
        if (!await router.Input.SendAsync(message, deadline.Token)) throw new Exception("Input rejected.");
        var reply = await (number < 0 ? negative : positive).Messages.ReceiveAsync(deadline.Token);
        if (!ReferenceEquals(reply.Value, payload) || payload.JsonBuilds != 0)
            throw new InvalidOperationException("Routing changed or serialized the payload.");
    }
    // An invalid local payload becomes error data; the next valid message still works.
    await router.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null), deadline.Token);
    var failure = await positive.Messages.ReceiveAsync(deadline.Token);
    if (!failure.IsError) throw new InvalidOperationException("Expected a routed processing error.");
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
    if (received.Value.Json.GetInt32() != 42) throw new InvalidOperationException("Source payload changed.");
    await source.Completion.WaitAsync(deadline.Token);
}
{
    var services = new ServiceCollection();
    var node = new NativePassThrough();
    services.AddComponentFactory("typed.pass", _ => node)
        .AddInputPort<NativePassThrough>("typed.pass", "Input", pass => pass.Block)
        .AddOutputPort<NativePassThrough>("typed.pass", "Output", pass => pass.Block);
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
    foreach (var address in new[] { "main.first", "main.second" })
        services.AddComponentFactory(address, _ => new Increment())
            .AddInputPort<Increment>(address, "Input", node => node.Input)
            .AddOutputPort<Increment>(address, "Output", node => node.Output)
            .AddEventPort<Increment>(address, node => node.Events);
    services.AddComponentFactory("main.native", _ => new NativePassThrough())
        .AddInputPort<NativePassThrough>("main.native", "Input", node => node.Block)
        .AddOutputPort<NativePassThrough>("main.native", "Output", node => node.Block);
    services.AddSingleton<LinkFactory>(ports =>
    {
        var link = ports.Link("main.first.Output", "main.native.Input");
        if (!ReferenceEquals(link.Output, ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.first.Output")) ||
            link.OutputAddress != "main.first.Output")
            throw new Exception("Link must expose the registered native source and its address.");
        ports.Link("main.native.Output", "main.second.Input");
    });
    var capture = Capture.Register(services, "main.second.Output");
    await using var provider = services.BuildServiceProvider();
    await using var appExecution = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
    await VerifyAsync(appExecution, capture, "main.first.Input", 2);
    await appExecution.StopAsync(deadline.Token);
}

{
    var services = new ServiceCollection();
    foreach (var address in new[] { "main.first", "main.second" })
        services.AddComponentFactory(address, _ => new Increment())
            .AddInputPort<Increment>(address, "Input", node => node.Input)
            .AddOutputPort<Increment>(address, "Output", node => node.Output)
            .AddEventPort<Increment>(address, node => node.Events);
    services.AddSingleton<LinkFactory>(ports =>
    {
        var node = (Increment)ports.GetRequiredKeyedService<IFlowNode>("main.second");
        if (!ReferenceEquals(ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.second.Input"), node.Input))
            throw new Exception("Input lookup must return the native block.");
        ports.Link("main.first.Output", "main.second.Input");
    });
    var capture = Capture.Register(services, "main.second.Output");
    await using var provider = services.BuildServiceProvider();
    var app = await provider.BuildApplicationAsync(cancellationToken: deadline.Token);
    await using var appExecution = app;
    await VerifyAsync(appExecution, capture, "main.first.Input", 2);
    await appExecution.StopAsync(deadline.Token);
}

Console.WriteLine($"CORE_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

async Task VerifyAsync(FlowApplication appExecution, Capture capture, string input, int increment)
{
    var nativeInput = ((Increment)appExecution.GetComponent(input[..input.LastIndexOf('.')])).Input;
    await appExecution.StartAsync(deadline.Token);
    await Task.WhenAll(Enumerable.Range(0, 16).Select(value =>
        nativeInput.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value))), deadline.Token)));
    var values = new List<int>();
    for (var index = 0; index < 16; index++)
        values.Add((await capture.Messages.ReceiveAsync(deadline.Token)).Value.Json.GetInt32());
    if (!values.Order().SequenceEqual(Enumerable.Range(increment, 16))) throw new Exception("Delivery lost or changed values.");
    await nativeInput.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1))), deadline.Token);
    var error = await capture.Messages.ReceiveAsync(deadline.Token);
    if (!error.IsError) throw new InvalidOperationException("Expected a flow error.");
    await nativeInput.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3))), deadline.Token);
    var recovered = await capture.Messages.ReceiveAsync(deadline.Token);
    if (recovered.IsError || recovered.Value.Json.GetInt32() != 3 + increment)
        throw new InvalidOperationException("Processing did not continue after the error.");
}

sealed class Increment() : FlowTransformNode(new ExecutionDataflowBlockOptions
    { BoundedCapacity = 2, MaxDegreeOfParallelism = 2 }, outputCapacity: 2)
{
    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        => TransformAsync(message, IncrementValueAsync);

    private static ValueTask<FlowValue> IncrementValueAsync(FlowValue input, CancellationToken cancellationToken)
    {
        var value = input.Json.GetInt32();
        if (value < 0) throw new InvalidOperationException("Negative input.");
        return ValueTask.FromResult<FlowValue>(new JsonFlowValue(JsonSerializer.SerializeToElement(value + 1)));
    }
}

sealed class NativePassThrough : IFlowNode
{

    public TransformBlock<FlowMessage, FlowMessage> Block { get; } = new(message => message,
        new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });
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

sealed class NumberRouter : FlowTransformNode
{
    public FlowOutput<FlowMessage> Negative { get; }

    public NumberRouter() => Negative = AddOutput<FlowMessage>();



    protected override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var value = message.Value as NumberValue
            ?? throw new InvalidOperationException("Expected NumberValue.");
        if (value.Number >= 0) return [message];
        await EmitAsync(Negative, message, Stopping);
        return [];
    }
}

sealed class SingleValueSource() : FlowSource(outputCapacity: 1)
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
        => await EmitAsync(FlowMessage.Create(new NumberValue(42)), cancellationToken);
}

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
