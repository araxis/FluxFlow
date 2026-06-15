using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Expectations.Tests;

public sealed class EventExpectationNodeTests
{
    [Fact]
    public async Task Expect_MatchesEventAndEmitsSatisfiedResult()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            ExpectationsComponentTypes.Expect,
            new
            {
                name = "failed-order",
                maxObservedEvents = 2,
                maxPreviewChars = 4,
                filter = new
                {
                    type = "operation.completed",
                    status = "failed",
                    subjectPrefix = "orders/"
                }
            },
            timeProvider);
        var input = GetInput(runtimeNode);
        var results = LinkResult(runtimeNode);
        var timestamp = new DateTimeOffset(2026, 6, 3, 7, 59, 0, TimeSpan.Zero);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(
            timestamp,
            "operation.completed",
            status: "ok",
            subject: "orders/1"));
        await input.Target.SendAsync(CreateEvent(
            timestamp.AddSeconds(1),
            "operation.completed",
            status: "failed",
            subject: "orders/2",
            payloadPreview: "abcdef"));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.EvaluatedAt.ShouldBe(timeProvider.GetUtcNow());
        result.Name.ShouldBe("failed-order");
        result.Kind.ShouldBe(EventExpectationResultKind.Expect);
        result.Satisfied.ShouldBeTrue();
        result.Matched.ShouldBeTrue();
        result.TimedOut.ShouldBeFalse();
        result.MatchedEvent.ShouldNotBeNull();
        result.MatchedEvent.Subject.ShouldBe("orders/2");
        result.MatchedEvent.PayloadPreview.ShouldBe("abcd");
        result.ObservedEvents.Count.ShouldBe(2);

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Expect_TimesOutWhenMatchIsNotObserved()
    {
        var timeout = TimeSpan.FromMilliseconds(500);
        var timeProvider = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 9, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            ExpectationsComponentTypes.Expect,
            new
            {
                timeoutMilliseconds = 500,
                filter = new
                {
                    type = "job.finished"
                }
            },
            timeProvider);
        var input = GetInput(runtimeNode);
        var results = LinkResult(runtimeNode);

        // Capture the timeout-timer registration before starting; the node arms its
        // Task.Delay from a background loop, so advancing before it is registered would
        // leave the delay pending forever (a hang under load).
        var timerScheduled = timeProvider.TimerScheduled;
        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(timeProvider.GetUtcNow(), "job.started"));

        // The send only queues the event; wait until the node has recorded it
        // so the timeout cannot resolve against an empty observation list.
        var node = runtimeNode.Node.ShouldBeOfType<Nodes.EventExpectationNode>();
        await WaitUntilAsync(() => node.ObservedEventCount == 1);

        // Only advance once the node has actually registered its timeout timer.
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Advance(timeout);

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Satisfied.ShouldBeFalse();
        result.Matched.ShouldBeFalse();
        result.TimedOut.ShouldBeTrue();
        result.ObservedEvents.ShouldHaveSingleItem();

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Guard_SucceedsOnTimeoutWhenNoMatchArrives()
    {
        var timeProvider = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            ExpectationsComponentTypes.Guard,
            new
            {
                timeoutMilliseconds = 1000,
                filter = new
                {
                    status = "failed"
                }
            },
            timeProvider);
        var results = LinkResult(runtimeNode);

        // The node arms its timeout delay from a background loop; wait for that timer
        // to be registered before advancing, otherwise the advance fires nothing.
        var timerScheduled = timeProvider.TimerScheduled;
        await runtimeNode.Node.StartAsync();
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Kind.ShouldBe(EventExpectationResultKind.Guard);
        result.Satisfied.ShouldBeTrue();
        result.Matched.ShouldBeFalse();
        result.TimedOut.ShouldBeTrue();

        GetInput(runtimeNode).Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Guard_FailsWhenMatchingEventArrives()
    {
        var runtimeNode = CreateNode(
            ExpectationsComponentTypes.Guard,
            new
            {
                filter = new
                {
                    channelPrefix = "events/",
                    attributes = new Dictionary<string, string>
                    {
                        ["severity"] = "critical"
                    }
                }
            });
        var input = GetInput(runtimeNode);
        var results = LinkResult(runtimeNode);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(
            new DateTimeOffset(2026, 6, 3, 11, 0, 0, TimeSpan.Zero),
            "operation.failed",
            channel: "events/orders",
            attributes: new Dictionary<string, string>
            {
                ["severity"] = "critical"
            }));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Kind.ShouldBe(EventExpectationResultKind.Guard);
        result.Satisfied.ShouldBeFalse();
        result.Matched.ShouldBeTrue();
        result.TimedOut.ShouldBeFalse();
        result.MatchedEvent.ShouldNotBeNull();
        result.MatchedEvent.Channel.ShouldBe("events/orders");

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Expect_EmitsNotMatchedWhenInputCompletes()
    {
        var runtimeNode = CreateNode(
            ExpectationsComponentTypes.Expect,
            new
            {
                filter = new
                {
                    typePrefix = "task."
                }
            });
        var input = GetInput(runtimeNode);
        var results = LinkResult(runtimeNode);

        await runtimeNode.Node.StartAsync();
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Satisfied.ShouldBeFalse();
        result.Matched.ShouldBeFalse();
        result.TimedOut.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dispose_CancelsTimeoutAndCompletesResult()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            ExpectationsComponentTypes.Expect,
            new
            {
                timeoutMilliseconds = 1000
            },
            timeProvider);
        var results = LinkResult(runtimeNode);

        // StartAsync arms the timeout delay. Time is never advanced, so the
        // delay stays pending and dispose must cancel it.
        await runtimeNode.Node.StartAsync();
        await runtimeNode.Node.ShouldBeAssignableTo<IAsyncDisposable>()!
            .DisposeAsync();

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Satisfied.ShouldBeFalse();
        result.TimedOut.ShouldBeFalse();
        await results.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Expectation_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                ExpectationsComponentTypes.Expect,
                new
                {
                    timeoutMilliseconds = 0
                }));

        exception.Message.ShouldContain("timeoutMilliseconds");
    }

    [Fact]
    public void RegisterExpectationsComponents_RegistersNodes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterExpectationsComponents();

        registry.TryGetFactory(ExpectationsComponentTypes.Expect, out _)
            .ShouldBeTrue();
        registry.TryGetFactory(ExpectationsComponentTypes.Guard, out _)
            .ShouldBeTrue();
    }

    private static RuntimeNode CreateNode(
        NodeType nodeType,
        object configuration,
        FakeTimeProvider? timeProvider = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterExpectationsComponents(options =>
            {
                if (timeProvider is not null)
                {
                    options.UseClock(timeProvider);
                }
            });
        registry.TryGetFactory(nodeType, out var factory).ShouldBeTrue();
        return factory(CreateContext(nodeType, configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(
        NodeType nodeType,
        object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("expectation"),
            new NodeDefinition
            {
                Type = nodeType,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<FlowEvent> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(ExpectationsComponentPorts.Input))
            .ShouldBeOfType<InputPort<FlowEvent>>();

    private static BufferBlock<EventExpectationResult> LinkResult(RuntimeNode runtimeNode)
    {
        var target = new BufferBlock<EventExpectationResult>();
        runtimeNode.FindOutput(new PortName(ExpectationsComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<EventExpectationResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }

    private static FlowEvent CreateEvent(
        DateTimeOffset timestamp,
        string type,
        string source = "processor",
        string? subject = null,
        string? status = null,
        string? channel = null,
        string? payloadPreview = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => new()
        {
            Timestamp = timestamp,
            Type = type,
            Source = source,
            Subject = subject,
            Status = status,
            Channel = channel,
            PayloadBytes = payloadPreview?.Length,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var startedAt = Environment.TickCount64;
        while (!condition())
        {
            (Environment.TickCount64 - startedAt).ShouldBeLessThan(5000);
            await Task.Delay(10);
        }
    }
}
