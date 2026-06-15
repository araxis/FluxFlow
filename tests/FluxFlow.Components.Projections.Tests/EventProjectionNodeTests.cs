using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Projections.Tests;

public sealed class EventProjectionNodeTests
{
    [Fact]
    public async Task Projection_CountsLatestPreviewAndRateForMatchingEvents()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateProjection(
            new
            {
                name = "errors",
                rateWindowSeconds = 10,
                maxPreviewChars = 4,
                filter = new
                {
                    type = "operation.completed",
                    subjectPrefix = "orders/",
                    status = "failed",
                    attributes = new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }
                }
            },
            timeProvider);
        var input = GetInput(runtimeNode);
        var output = LinkOutput(runtimeNode);
        var start = new DateTimeOffset(2026, 6, 3, 7, 59, 50, TimeSpan.Zero);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(
            start,
            "operation.completed",
            subject: "orders/1",
            status: "failed",
            payloadPreview: "abcdef",
            attributes: new Dictionary<string, string> { ["tenant"] = "north" }));
        await input.Target.SendAsync(CreateEvent(
            start.AddSeconds(5),
            "operation.completed",
            subject: "orders/2",
            status: "ok",
            payloadPreview: "ignored",
            attributes: new Dictionary<string, string> { ["tenant"] = "north" }));
        await input.Target.SendAsync(CreateEvent(
            start.AddSeconds(9),
            "operation.completed",
            subject: "orders/3",
            status: "failed",
            payloadPreview: "xyz",
            attributes: new Dictionary<string, string> { ["tenant"] = "north" }));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        first.Name.ShouldBe("errors");
        first.Timestamp.ShouldBe(timeProvider.GetUtcNow());
        first.ObservedCount.ShouldBe(1);
        first.MatchedCount.ShouldBe(1);
        first.CurrentRate.ShouldBe(0.1);
        first.Latest.ShouldNotBeNull();
        first.Latest.PayloadPreview.ShouldBe("abcd");

        second.ObservedCount.ShouldBe(3);
        second.MatchedCount.ShouldBe(2);
        second.CurrentRate.ShouldBe(0.2);
        second.Latest.ShouldNotBeNull();
        second.Latest.Subject.ShouldBe("orders/3");
        second.Latest.PayloadPreview.ShouldBe("xyz");
    }

    [Fact]
    public async Task Projection_FiltersByChannelExclusionSourceNodeAndComponent()
    {
        var nodeId = FlowNodeId.New();
        var runtimeNode = CreateProjection(new
        {
            filter = new
            {
                channelPrefix = "events/",
                excludedChannelPrefix = "events/debug",
                source = "processor",
                sourceNodeId = nodeId.ToString(),
                componentId = "component-a"
            }
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput(runtimeNode);
        var timestamp = new DateTimeOffset(2026, 6, 3, 9, 0, 0, TimeSpan.Zero);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(
            timestamp,
            "item.observed",
            source: "processor",
            channel: "events/debug/trace",
            sourceNodeId: nodeId,
            attributes: new Dictionary<string, string> { ["componentId"] = "component-a" }));
        await input.Target.SendAsync(CreateEvent(
            timestamp.AddSeconds(1),
            "item.observed",
            source: "processor",
            channel: "events/live",
            sourceNodeId: nodeId,
            attributes: new Dictionary<string, string> { ["componentId"] = "component-a" }));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.ObservedCount.ShouldBe(2);
        snapshot.MatchedCount.ShouldBe(1);
        snapshot.Latest.ShouldNotBeNull();
        snapshot.Latest.Channel.ShouldBe("events/live");
        snapshot.Latest.SourceNodeId.ShouldBe(nodeId.ToString());
    }

    [Fact]
    public async Task Projection_AppliesTimeRange()
    {
        var from = new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero);
        var to = from.AddMinutes(1);
        var runtimeNode = CreateProjection(new
        {
            filter = new
            {
                from,
                to
            }
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput(runtimeNode);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(from.AddSeconds(-1), "event.before"));
        await input.Target.SendAsync(CreateEvent(from.AddSeconds(30), "event.inside"));
        await input.Target.SendAsync(CreateEvent(to.AddSeconds(1), "event.after"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.ObservedCount.ShouldBe(2);
        snapshot.MatchedCount.ShouldBe(1);
        snapshot.Latest.ShouldNotBeNull();
        snapshot.Latest.Type.ShouldBe("event.inside");
    }

    [Fact]
    public async Task Projection_EmitsFinalSnapshotWhenConfigured()
    {
        var timestamp = new DateTimeOffset(2026, 6, 3, 11, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(timestamp);
        var runtimeNode = CreateProjection(new
        {
            rateWindowSeconds = 10,
            emitEveryMatch = false,
            emitFinalSnapshot = true,
            filter = new
            {
                typePrefix = "task."
            }
        },
        timeProvider);
        var input = GetInput(runtimeNode);
        var output = LinkOutput(runtimeNode);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(timestamp, "task.started"));
        await input.Target.SendAsync(CreateEvent(timestamp.AddSeconds(1), "task.completed"));
        timeProvider.SetUtcNow(timestamp.AddSeconds(20));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Timestamp.ShouldBe(timeProvider.GetUtcNow());
        snapshot.MatchedCount.ShouldBe(2);
        snapshot.CurrentRate.ShouldBe(0.2);
        snapshot.Latest.ShouldNotBeNull();
        snapshot.Latest.Type.ShouldBe("task.completed");
    }

    [Fact]
    public async Task Projection_FinalSnapshotKeepsRateForReplayedEventTimestamps()
    {
        var eventTime = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(eventTime.AddDays(30));
        var runtimeNode = CreateProjection(new
        {
            rateWindowSeconds = 10,
            emitEveryMatch = false,
            emitFinalSnapshot = true
        },
        timeProvider);
        var input = GetInput(runtimeNode);
        var output = LinkOutput(runtimeNode);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(eventTime, "replayed.first"));
        await input.Target.SendAsync(CreateEvent(eventTime.AddSeconds(1), "replayed.second"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The rate window is trimmed against the last event timestamp, so replayed
        // streams with old event timestamps keep a meaningful final rate.
        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Timestamp.ShouldBe(timeProvider.GetUtcNow());
        snapshot.MatchedCount.ShouldBe(2);
        snapshot.CurrentRate.ShouldBe(0.2);
    }

    [Fact]
    public async Task Projection_TreatsNullFilterAsMatchAll()
    {
        var runtimeNode = CreateProjection(new
        {
            filter = (object?)null
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput(runtimeNode);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(
            new DateTimeOffset(2026, 6, 3, 11, 30, 0, TimeSpan.Zero),
            "operation.completed"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.MatchedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Projection_EmitsDiagnosticsForMatches()
    {
        var runtimeNode = CreateProjection(new { });
        var input = GetInput(runtimeNode);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });
        var timestamp = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(CreateEvent(timestamp, "first"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(ProjectionDiagnosticNames.ProjectionUpdated);
        diagnostic.Attributes["matchedCount"].ShouldBe(1L);
    }

    [Fact]
    public void Projection_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateProjection(new
            {
                rateWindowSeconds = 0
            }));

        exception.Message.ShouldContain("rateWindowSeconds");
    }

    [Fact]
    public void RegisterProjectionsComponents_RegistersNode()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterProjectionsComponents();

        registry.TryGetFactory(ProjectionsComponentTypes.EventProjection, out _)
            .ShouldBeTrue();
    }

    [Fact]
    public void EventFilterMatcher_MatchesAttributeAndPrefixes()
    {
        var flowEvent = CreateEvent(
            new DateTimeOffset(2026, 6, 3, 13, 0, 0, TimeSpan.Zero),
            "file.created",
            subject: "files/inbox/report.json",
            channel: "events/files",
            attributes: new Dictionary<string, string>
            {
                ["kind"] = "document"
            });

        EventFilterMatcher.IsMatch(
            flowEvent,
            new EventFilter
            {
                TypePrefix = "file.",
                SubjectPrefix = "files/inbox",
                ChannelPrefix = "events/",
                Attributes = new Dictionary<string, string>
                {
                    ["kind"] = "document"
                }
            }).ShouldBeTrue();
    }

    private static RuntimeNode CreateProjection(
        object configuration,
        FakeTimeProvider? timeProvider = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterProjectionsComponents(options =>
            {
                if (timeProvider is not null)
                {
                    options.UseClock(timeProvider);
                }
            });
        registry.TryGetFactory(ProjectionsComponentTypes.EventProjection, out var factory)
            .ShouldBeTrue();
        return factory(CreateContext(ProjectionsComponentTypes.EventProjection, configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(
        NodeType nodeType,
        object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("projection"),
            new NodeDefinition
            {
                Type = nodeType,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<FlowEvent> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(ProjectionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<FlowEvent>>();

    private static BufferBlock<EventProjectionSnapshot> LinkOutput(RuntimeNode runtimeNode)
    {
        var target = new BufferBlock<EventProjectionSnapshot>();
        runtimeNode.FindOutput(new PortName(ProjectionsComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<EventProjectionSnapshot>(
                    new PortAddress("test", new NodeName("snapshots"), new PortName("Input")),
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
        FlowNodeId? sourceNodeId = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => new()
        {
            Timestamp = timestamp,
            Type = type,
            Source = source,
            SourceNodeId = sourceNodeId,
            Subject = subject,
            Status = status,
            Channel = channel,
            PayloadBytes = payloadPreview?.Length,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };
}
