using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerThrottleNodeTests
{
    [Fact]
    public async Task Throttle_EmitsFirstInputBeforeIntervalByDefault()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                intervalMilliseconds = 1000
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync("one");
        input.Target.Complete();

        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(250));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        value.ShouldBe("one");
    }

    [Fact]
    public async Task Throttle_SpacesLaterInputs()
    {
        var runtimeNode = CreateNode(
            options => options.RegisterType<InputMessage>("message"),
            new
            {
                inputType = "message",
                name = "rate",
                intervalMilliseconds = 45,
                boundedCapacity = 4
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var output = new BufferBlock<InputMessage>();
        LinkOutput(runtimeNode, output);
        var stopwatch = Stopwatch.StartNew();

        await input.Target.SendAsync(new InputMessage("one"));
        await input.Target.SendAsync(new InputMessage("two"));
        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Value.ShouldBe("one");
        second.Value.ShouldBe("two");
        stopwatch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(30);
    }

    [Fact]
    public async Task Throttle_CanDelayFirstInput()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                intervalMilliseconds = 35,
                emitFirstImmediately = false
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);
        var startedAt = DateTimeOffset.UtcNow;

        await input.Target.SendAsync("hello");
        input.Target.Complete();
        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        value.ShouldBe("hello");
        DateTimeOffset.UtcNow.ShouldBeGreaterThanOrEqualTo(startedAt.AddMilliseconds(25));
    }

    [Fact]
    public async Task Throttle_PreservesOrder()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "int",
                intervalMilliseconds = 1,
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
    public async Task Throttle_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                intervalMilliseconds = 1
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
        diagnostic.Name.ShouldBe(TimerDiagnosticNames.ThrottleEmitted);
        diagnostic.Attributes["inputType"].ShouldBe("string");
        diagnostic.Attributes["sequence"].ShouldBe(1L);
    }

    [Fact]
    public async Task Throttle_DisposeDrainsAndCompletesOutput()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                intervalMilliseconds = 1
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
    public void Throttle_RejectsMissingInterval()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(_ => { }, new { inputType = "string" }));

        exception.Message.ShouldContain("interval");
    }

    [Fact]
    public void Throttle_RejectsNonPositiveInterval()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    intervalMilliseconds = 0
                }));

        exception.Message.ShouldContain("interval");
    }

    [Fact]
    public void Throttle_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    intervalMilliseconds = 1,
                    boundedCapacity = 0
                }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Throttle_RejectsUnknownInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "message",
                    intervalMilliseconds = 1
                }));

        exception.Message.ShouldContain("message");
    }

    [Fact]
    public void Throttle_RejectsDuplicateIntervalOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    interval = TimeSpan.FromMilliseconds(1),
                    intervalMilliseconds = 1
                }));

        exception.Message.ShouldContain("interval");
    }

    private static RuntimeNode CreateNode(
        Action<TimerComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTimerComponents(configure);
        registry.TryGetFactory(TimerComponentTypes.Throttle, out var factory).ShouldBeTrue();
        return factory(TimerTestHost.CreateContext(
            TimerComponentTypes.Throttle,
            configuration,
            "throttle"));
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
