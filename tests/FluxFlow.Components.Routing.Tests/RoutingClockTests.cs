using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
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
        var clock = new RecordingRoutingClock(timestamp);
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
        var clock = new RecordingRoutingClock(timestamp);
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
        var clock = new RecordingRoutingClock(startedAt);
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

        await input.Target.SendAsync("first");
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(25)]);
        window.StartedAt.ShouldBe(startedAt);
        window.EmittedAt.ShouldBe(startedAt.AddMilliseconds(25));
        window.Duration.ShouldBe(TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task Join_UsesConfiguredClockForTimeoutDelay()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:03Z");
        var clock = new RecordingRoutingClock(startedAt);
        var runtimeNode = CreateJoinNode(
            clock,
            new { timeoutMilliseconds = 25 });
        var left = GetInput<LeftMessage>(runtimeNode, RoutingComponentPorts.Left);
        var right = GetInput<RightMessage>(runtimeNode, RoutingComponentPorts.Right);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!.LinkToDiscard();
        var timeouts = new BufferBlock<FlowJoinTimeout<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        await left.Target.SendAsync(new LeftMessage("A-100"));
        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(25)]);
        timeout.ReceivedAt.ShouldBe(startedAt);
        timeout.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(25));
    }

    [Fact]
    public async Task Correlation_UsesConfiguredClockForMatchTimestamps()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:04Z");
        var clock = new RecordingRoutingClock(timestamp);
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
        RecordingRoutingClock clock,
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

    private static RuntimeNode CreateCorrelationNode(RecordingRoutingClock clock)
        => CreateNode(
            RoutingComponentTypes.Correlation,
            new
            {
                keyExpression = "key",
                sideExpression = "side",
                inputType = "clock.correlation"
            },
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

    private sealed class RecordingRoutingClock(DateTimeOffset utcNow) : IRoutingClock
    {
        private readonly object _gate = new();
        private readonly List<TimeSpan> _delays = [];
        private DateTimeOffset _utcNow = utcNow;

        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_gate)
                {
                    return _utcNow;
                }
            }
        }

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_gate)
                {
                    return _delays.ToArray();
                }
            }
        }

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _delays.Add(delay);
                if (delay > TimeSpan.Zero)
                {
                    _utcNow += delay;
                }
            }

            return ValueTask.CompletedTask;
        }
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
