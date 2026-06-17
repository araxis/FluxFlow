using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerDebounceNodeTests
{
    [Fact]
    public async Task Debounce_EmitsLatestInputAfterQuietPeriod()
    {
        var runtimeNode = CreateNode(
            options => options.RegisterType<InputMessage>("message"),
            new
            {
                inputType = "message",
                name = "quiet",
                quietPeriodMilliseconds = 40,
                boundedCapacity = 4
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var output = new BufferBlock<InputMessage>();
        LinkOutput(runtimeNode, output);
        var startedAt = DateTimeOffset.UtcNow;

        await input.Target.SendAsync(new InputMessage("one"));
        await input.Target.SendAsync(new InputMessage("two"));

        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        value.Value.ShouldBe("two");
        DateTimeOffset.UtcNow.ShouldBeGreaterThanOrEqualTo(startedAt.AddMilliseconds(25));
    }

    [Fact]
    public async Task Debounce_FlushesPendingInputOnCompletion()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                quietPeriodMilliseconds = 1000
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync("one");
        input.Target.Complete();

        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(250));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        value.ShouldBe("one");
    }

    [Fact]
    public async Task Debounce_EmitsLatestPerQuietWindow()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "int",
                quietPeriodMilliseconds = 25,
                boundedCapacity = 8
            });
        var input = runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<int>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await input.Target.SendAsync(3);
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        first.ShouldBe(2);
        second.ShouldBe(3);
    }

    [Fact]
    public async Task Debounce_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                quietPeriodMilliseconds = 1
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
        diagnostic.Name.ShouldBe(TimerDiagnosticNames.DebounceEmitted);
        diagnostic.Attributes["inputType"].ShouldBe("string");
        diagnostic.Attributes["sequence"].ShouldBe(1L);
    }

    [Fact]
    public async Task Debounce_DisposeFlushesAndCompletesOutput()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                quietPeriodMilliseconds = 1000
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
    public async Task Debounce_DisposeAfterFaultDoesNotThrow()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                quietPeriodMilliseconds = 1
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
    public void Debounce_RejectsMissingQuietPeriod()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(_ => { }, new { inputType = "string" }));

        exception.Message.ShouldContain("quietPeriod");
    }

    [Fact]
    public void Debounce_RejectsNonPositiveQuietPeriod()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    quietPeriodMilliseconds = 0
                }));

        exception.Message.ShouldContain("quietPeriod");
    }

    [Fact]
    public void Debounce_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    quietPeriodMilliseconds = 1,
                    boundedCapacity = 0
                }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Debounce_RejectsUnknownInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "message",
                    quietPeriodMilliseconds = 1
                }));

        exception.Message.ShouldContain("message");
    }

    [Fact]
    public void Debounce_RejectsDuplicateQuietPeriodOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    quietPeriod = TimeSpan.FromMilliseconds(1),
                    quietPeriodMilliseconds = 1
                }));

        exception.Message.ShouldContain("quietPeriod");
    }

    private static RuntimeNode CreateNode(
        Action<TimerComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTimerComponents(configure);
        registry.TryGetFactory(TimerComponentTypes.Debounce, out var factory).ShouldBeTrue();
        return factory(TimerTestHost.CreateContext(
            TimerComponentTypes.Debounce,
            configuration,
            "debounce"));
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
}
