using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class RoutingClockTests
{
    [Fact]
    public async Task Switch_UsesConfiguredClockForResultAndRouteEnvelope()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var runtimeNode = CreateNode(
            RoutingComponentTypes.Switch,
            new
            {
                expression = "route",
                routes = new[] { "matched" },
                emitRouteEnvelope = true
            },
            options => options
                .UseClock(clock)
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, _, _) => "matched")));
        var input = GetInput<object>(runtimeNode, RoutingComponentPorts.Input);
        var result = new BufferBlock<FlowSwitchResult<object>>();
        var routed = new BufferBlock<FlowRoute<object>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Result, result);
        LinkOutput(runtimeNode, RoutingComponentPorts.Routed, routed);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await result.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).EvaluatedAt
            .ShouldBe(timestamp);
        (await routed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).RoutedAt
            .ShouldBe(timestamp);
    }

    [Fact]
    public async Task Merge_UsesConfiguredClockForOutputTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:01Z");
        var clock = new FakeTimeProvider(timestamp);
        var runtimeNode = CreateNode(
            RoutingComponentTypes.Merge,
            new
            {
                inputType = "string",
                inputs = new[] { "Only" }
            },
            options => options.UseClock(clock));
        var input = GetInput<string>(runtimeNode, "Only");
        var output = new BufferBlock<FlowMergeItem<string>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Output, output);

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ReceivedAt
            .ShouldBe(timestamp);
    }

    [Fact]
    public async Task Window_UsesConfiguredClockForTimeWindowDelay()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:02Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        var runtimeNode = CreateNode(
            RoutingComponentTypes.Window,
            new
            {
                inputType = "string",
                timeMilliseconds = 25,
                boundedCapacity = 8
            },
            options => options.UseClock(clock));
        var input = GetInput<string>(runtimeNode, RoutingComponentPorts.Input);
        var output = new BufferBlock<FlowWindow<string>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Output, output);

        var timerScheduled = clock.NextTimerScheduled;
        await input.Target.SendAsync("first");
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(5));
        // The time window stays pending until the fake clock is advanced; nothing
        // should have been emitted while the delay is outstanding.
        output.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        window.StartedAt.ShouldBe(startedAt);
        window.EmittedAt.ShouldBe(startedAt.AddMilliseconds(25));
        window.Duration.ShouldBe(TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task Join_UsesConfiguredClockForTimeoutDelay()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:03Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        var runtimeNode = CreateJoinNode(
            clock,
            new { timeoutMilliseconds = 25 });
        var left = GetInput<LeftMessage>(runtimeNode, RoutingComponentPorts.Left);
        var right = GetInput<RightMessage>(runtimeNode, RoutingComponentPorts.Right);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!.LinkToDiscard();
        var timeouts = new BufferBlock<FlowJoinTimeout<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        var timerScheduled = clock.NextTimerScheduled;
        await left.Target.SendAsync(new LeftMessage("A-100"));
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(5));
        // The timeout timer waits on the fake clock; no timeout fires until it advances.
        timeouts.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        timeout.ReceivedAt.ShouldBe(startedAt);
        timeout.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(25));
    }

    [Fact]
    public async Task Correlation_UsesConfiguredClockForTimeoutDelay()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        var runtimeNode = CreateCorrelationNode(
            clock,
            new { timeoutMilliseconds = 25 });
        var input = GetInput<CorrelationMessage>(runtimeNode, RoutingComponentPorts.Input);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        var timeouts = new BufferBlock<FlowCorrelationTimeout<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        var timerScheduled = clock.NextTimerScheduled;
        await input.Target.SendAsync(new CorrelationMessage("A-100", "request"));
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(5));
        // The pending request waits for the timeout timer; advancing the clock fires it.
        timeouts.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe("request");
        timeout.ReceivedAt.ShouldBe(startedAt);
        timeout.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(25));
    }

    [Fact]
    public async Task Correlation_UsesConfiguredClockForMatchTimestamps()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:04Z");
        // Never advanced: equivalent to the old fixed clock whose delay never
        // completes, so the match is driven purely by the two inputs.
        var clock = new FakeTimeProvider(timestamp);
        var runtimeNode = CreateCorrelationNode(clock);
        var input = GetInput<CorrelationMessage>(runtimeNode, RoutingComponentPorts.Input);
        var matched = new BufferBlock<FlowCorrelationMatch<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request"));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "response"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var match = await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        match.RequestReceivedAt.ShouldBe(timestamp);
        match.ResponseReceivedAt.ShouldBe(timestamp);
        match.MatchedAt.ShouldBe(timestamp);
    }

    private static RuntimeNode CreateNode(
        NodeType type,
        object configuration,
        Action<Options.RoutingComponentOptions> configure)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(configure);
        registry.TryGetFactory(type, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(type, configuration));
    }

    private static RuntimeNode CreateJoinNode(
        FakeTimeProvider clock,
        object overrides)
    {
        var configuration = RoutingTestHost.MergeConfiguration(
            new
            {
                leftKeyExpression = "key",
                rightKeyExpression = "key",
                leftInputType = "clock.left",
                rightInputType = "clock.right"
            },
            overrides);
        return CreateNode(
            RoutingComponentTypes.Join,
            configuration,
            options => options
                .UseClock(clock)
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, context, _) => context.Variables["key"]))
                .RegisterType<LeftMessage>("clock.left")
                .RegisterType<RightMessage>("clock.right")
                .UseContextFactory(new LeftContextFactory())
                .UseContextFactory(new RightContextFactory()));
    }

    private static RuntimeNode CreateCorrelationNode(
        FakeTimeProvider clock,
        object? overrides = null)
        => CreateNode(
            RoutingComponentTypes.Correlation,
            RoutingTestHost.MergeConfiguration(
                new
                {
                    keyExpression = "key",
                    sideExpression = "side",
                    inputType = "clock.correlation"
                },
                overrides ?? new { }),
            options => options
                .UseClock(clock)
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (expression, context, _) => expression switch
                    {
                        "key" => context.Variables["key"],
                        "side" => context.Variables["side"],
                        _ => null
                    }))
                .RegisterType<CorrelationMessage>("clock.correlation")
                .UseContextFactory(new CorrelationContextFactory()));

    private static InputPort<T> GetInput<T>(
        RuntimeNode runtimeNode,
        string portName)
        => runtimeNode.FindInput(new PortName(portName))
            .ShouldBeOfType<InputPort<T>>();

    private static void LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName,
        BufferBlock<T> target)
    {
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName(portName), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private sealed record LeftMessage(string Key);

    private sealed record RightMessage(string Key);

    private sealed record CorrelationMessage(string Key, string Side);

    private sealed class LeftContextFactory : IFlowMapContextFactory<LeftMessage>
    {
        public FlowMapContext Create(LeftMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["key"] = input.Key
                }
            };
    }

    private sealed class RightContextFactory : IFlowMapContextFactory<RightMessage>
    {
        public FlowMapContext Create(RightMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["key"] = input.Key
                }
            };
    }

    private sealed class CorrelationContextFactory : IFlowMapContextFactory<CorrelationMessage>
    {
        public FlowMapContext Create(CorrelationMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["key"] = input.Key,
                    ["side"] = input.Side
                }
            };
    }
}
