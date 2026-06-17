using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var delayed = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await DrainUntilCompletedAsync(output)).ShouldBe(["hello"]);
        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
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

        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        (await DrainUntilCompletedAsync(output)).ShouldBe(["one"]);
    }

    [Fact]
    public async Task Delay_BurstEmitsItemsWithConstantOffset()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            options => options.UseClock(clock),
            new
            {
                inputType = "int",
                delayMilliseconds = 50,
                boundedCapacity = 8
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<int>();
        LinkOutput(runtimeNode, output);

        // Capture the shared delay's registration before sending the burst that arms it.
        var scheduled = clock.TimerScheduled;
        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        await input.Target.SendAsync(3);
        input.Target.Complete();
        // The intake block stamps every item on arrival with the same due time, so the
        // whole burst shares one pending delay. Wait until the node has actually armed
        // that Task.Delay before advancing the clock to release it; the later items then
        // see a non-positive remaining delay and emit without waiting again.
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(50));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await DrainUntilCompletedAsync(output)).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Delay_ErrorsPortReceivesPerMessageFailures()
    {
        var clock = new ThrowingTimeProvider();
        var runtimeNode = CreateNode(
            options => options.UseClock(clock),
            new
            {
                inputType = "string",
                delayMilliseconds = 5
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<string>();
        var errors = new BufferBlock<FlowError>();
        LinkOutput(runtimeNode, output);
        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Errors))!
            .TryLinkTo(
                new InputPort<FlowError>(
                    new PortAddress("test", new NodeName("errors"), new PortName("Input")),
                    errors),
                propagateCompletion: true,
                out var linkError);
        linkError.ShouldBeNull();

        await input.Target.SendAsync("bad");
        await input.Target.SendAsync("good");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(TimerErrorCodes.DelayFailed);
        (await DrainUntilCompletedAsync(output)).ShouldBe(["good"]);
    }

    [Fact]
    public async Task Delay_DisposeAfterFaultDoesNotThrow()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                delayMilliseconds = 1
            });
        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Output))!
            .LinkToDiscard();

        runtimeNode.Node.Fault(new InvalidOperationException("boom"));
        await runtimeNode.Node.ShouldBeAssignableTo<IAsyncDisposable>()!
            .DisposeAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.Completion);
    }

    [Fact]
    public async Task Delay_DisposePromptlyCancelsPendingDelays()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "int",
                delayMilliseconds = 30000,
                boundedCapacity = 8
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<int>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        await runtimeNode.Node.ShouldBeAssignableTo<IAsyncDisposable>()!
            .DisposeAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
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
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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

    // FakeTimeProvider cannot be told to throw from a delay, so this bespoke provider
    // throws from the timer/delay path exactly once to exercise clock-fault handling.
    // GetUtcNow returns a fixed instant so the node sees a positive remaining delay and
    // actually reaches Task.Delay (which creates a timer).
    private sealed class ThrowingTimeProvider : TimeProvider
    {
        private int _calls;

        public override DateTimeOffset GetUtcNow()
            => new(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException("clock failed");
            }

            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
