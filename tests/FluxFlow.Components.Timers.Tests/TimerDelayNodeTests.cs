using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerDelayNodeTests
{
    [Fact]
    public async Task Delay_EmitsInputAfterConfiguredDelay()
    {
        var runtimeNode = CreateNode(
            options => options.RegisterType<InputMessage>("message"),
            new
            {
                inputType = "message",
                name = "hold",
                delayMilliseconds = 35,
                boundedCapacity = 4
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var output = new BufferBlock<InputMessage>();
        LinkOutput(runtimeNode, output);
        var message = new InputMessage("one");
        var startedAt = DateTimeOffset.UtcNow;

        await input.Target.SendAsync(message);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var delayed = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        delayed.ShouldBe(message);
        DateTimeOffset.UtcNow.ShouldBeGreaterThan(startedAt.AddMilliseconds(20));
    }

    [Fact]
    public async Task Delay_PreservesOrder()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "int",
                delayMilliseconds = 1,
                boundedCapacity = 8
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<int>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        await input.Target.SendAsync(3);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Delay_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                delayMilliseconds = 0
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<string>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        LinkOutput(runtimeNode, output);
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await input.Target.SendAsync("hello");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldBe(["hello"]);
        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(TimerDiagnosticNames.DelayEmitted);
        diagnostic.Attributes["inputType"].ShouldBe("string");
    }

    [Fact]
    public async Task Delay_DisposeDrainsAndCompletesOutput()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                delayMilliseconds = 0
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync("one");
        await runtimeNode.Node.ShouldBeAssignableTo<IAsyncDisposable>()!
            .DisposeAsync();

        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await DrainUntilCompletedAsync(output)).ShouldBe(["one"]);
    }

    [Fact]
    public void Delay_RejectsMissingDelay()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(_ => { }, new { inputType = "string" }));

        exception.Message.ShouldContain("delay");
    }

    [Fact]
    public void Delay_RejectsNegativeDelay()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    delayMilliseconds = -1
                }));

        exception.Message.ShouldContain("delay");
    }

    [Fact]
    public void Delay_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    delayMilliseconds = 1,
                    boundedCapacity = 0
                }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Delay_RejectsUnknownInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "message",
                    delayMilliseconds = 1
                }));

        exception.Message.ShouldContain("message");
    }

    [Fact]
    public void Delay_RejectsDuplicateDurationOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    delay = TimeSpan.FromMilliseconds(1),
                    delayMilliseconds = 1
                }));

        exception.Message.ShouldContain("delay");
    }

    private static RuntimeNode CreateNode(
        Action<TimerComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTimerComponents(configure);
        registry.TryGetFactory(TimerComponentTypes.Delay, out var factory).ShouldBeTrue();
        return factory(TimerTestHost.CreateContext(
            TimerComponentTypes.Delay,
            configuration,
            "delay"));
    }

    private static void LinkOutput<T>(
        RuntimeNode runtimeNode,
        BufferBlock<T> target)
    {
        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var values = new List<T>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private sealed record InputMessage(string Value);
}
