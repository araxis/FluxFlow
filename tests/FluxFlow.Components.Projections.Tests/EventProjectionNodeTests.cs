using FluxFlow.Components.Projections;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Projections.Tests;

// Every test news the node directly — no engine, no registry. Events travel as
// FlowMessage<ProjectionEvent> envelopes; the correlation id flows event -> snapshot.
public sealed class EventProjectionNodeTests
{
    [Fact]
    public async Task Projection_CountsLatestPreviewAndRateForMatchingEvents()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.Zero));
        await using var node = new EventProjectionNode(
            new EventProjectionOptions
            {
                Name = "errors",
                RateWindowSeconds = 10,
                MaxPreviewChars = 4,
                Filter = new EventFilter
                {
                    Type = "operation.completed",
                    SubjectPrefix = "orders/",
                    Status = "failed",
                    Attributes = new Dictionary<string, string> { ["tenant"] = "north" }
                }
            },
            timeProvider);
        var output = Sink(node.Output);
        var start = new DateTimeOffset(2026, 6, 3, 7, 59, 50, TimeSpan.Zero);

        var firstSent = FlowMessage.Create(CreateEvent(
            start,
            "operation.completed",
            subject: "orders/1",
            status: "failed",
            payloadPreview: "abcdef",
            attributes: new Dictionary<string, string> { ["tenant"] = "north" }));
        await node.Input.SendAsync(firstSent);
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            start.AddSeconds(5),
            "operation.completed",
            subject: "orders/2",
            status: "ok",
            payloadPreview: "ignored",
            attributes: new Dictionary<string, string> { ["tenant"] = "north" })));
        var thirdSent = FlowMessage.Create(CreateEvent(
            start.AddSeconds(9),
            "operation.completed",
            subject: "orders/3",
            status: "failed",
            payloadPreview: "xyz",
            attributes: new Dictionary<string, string> { ["tenant"] = "north" }));
        await node.Input.SendAsync(thirdSent);

        var first = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        var second = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        // Correlation flows from the triggering event to the snapshot it produced.
        first.CorrelationId.ShouldBe(firstSent.CorrelationId);
        first.Payload.Name.ShouldBe("errors");
        first.Payload.Timestamp.ShouldBe(timeProvider.GetUtcNow());
        first.Payload.ObservedCount.ShouldBe(1);
        first.Payload.MatchedCount.ShouldBe(1);
        first.Payload.CurrentRate.ShouldBe(0.1);
        first.Payload.Latest.ShouldNotBeNull();
        first.Payload.Latest.PayloadPreview.ShouldBe("abcd");

        second.CorrelationId.ShouldBe(thirdSent.CorrelationId);
        second.Payload.ObservedCount.ShouldBe(3);
        second.Payload.MatchedCount.ShouldBe(2);
        second.Payload.CurrentRate.ShouldBe(0.2);
        second.Payload.Latest.ShouldNotBeNull();
        second.Payload.Latest.Subject.ShouldBe("orders/3");
        second.Payload.Latest.PayloadPreview.ShouldBe("xyz");
    }

    [Fact]
    public async Task Output_FansOutEverySnapshotToEveryConsumer()
    {
        await using var node = new EventProjectionNode();
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);
        var at = new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.Zero);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(at, "a")));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(at.AddSeconds(1), "b")));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.MatchedCount.ShouldBe(1);
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.MatchedCount.ShouldBe(2);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.MatchedCount.ShouldBe(1);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.MatchedCount.ShouldBe(2);
    }

    [Fact]
    public async Task Projection_FiltersByChannelExclusionSourceNodeAndComponent()
    {
        var nodeId = Guid.NewGuid().ToString();
        await using var node = new EventProjectionNode(new EventProjectionOptions
        {
            Filter = new EventFilter
            {
                ChannelPrefix = "events/",
                ExcludedChannelPrefix = "events/debug",
                Source = "processor",
                SourceNodeId = nodeId,
                ComponentId = "component-a"
            }
        });
        var output = Sink(node.Output);
        var timestamp = new DateTimeOffset(2026, 6, 3, 9, 0, 0, TimeSpan.Zero);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            timestamp,
            "item.observed",
            source: "processor",
            channel: "events/debug/trace",
            sourceNodeId: nodeId,
            attributes: new Dictionary<string, string> { ["componentId"] = "component-a" })));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            timestamp.AddSeconds(1),
            "item.observed",
            source: "processor",
            channel: "events/live",
            sourceNodeId: nodeId,
            attributes: new Dictionary<string, string> { ["componentId"] = "component-a" })));

        var snapshot = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        snapshot.ObservedCount.ShouldBe(2);
        snapshot.MatchedCount.ShouldBe(1);
        snapshot.Latest.ShouldNotBeNull();
        snapshot.Latest.Channel.ShouldBe("events/live");
        snapshot.Latest.SourceNodeId.ShouldBe(nodeId);
    }

    [Fact]
    public async Task Projection_AppliesTimeRange()
    {
        var from = new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero);
        var to = from.AddMinutes(1);
        await using var node = new EventProjectionNode(new EventProjectionOptions
        {
            Filter = new EventFilter { From = from, To = to }
        });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(from.AddSeconds(-1), "event.before")));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(from.AddSeconds(30), "event.inside")));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(to.AddSeconds(1), "event.after")));

        var snapshot = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
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
        await using var node = new EventProjectionNode(
            new EventProjectionOptions
            {
                RateWindowSeconds = 10,
                EmitEveryMatch = false,
                EmitFinalSnapshot = true,
                Filter = new EventFilter { TypePrefix = "task." }
            },
            timeProvider);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(timestamp, "task.started")));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(timestamp.AddSeconds(1), "task.completed")));
        timeProvider.SetUtcNow(timestamp.AddSeconds(20));
        await node.CompleteWithFinalSnapshotAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
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
        await using var node = new EventProjectionNode(
            new EventProjectionOptions
            {
                RateWindowSeconds = 10,
                EmitEveryMatch = false,
                EmitFinalSnapshot = true
            },
            timeProvider);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(eventTime, "replayed.first")));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(eventTime.AddSeconds(1), "replayed.second")));
        await node.CompleteWithFinalSnapshotAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The rate window is trimmed against the last event timestamp, so replayed
        // streams with old event timestamps keep a meaningful final rate.
        var snapshot = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        snapshot.Timestamp.ShouldBe(timeProvider.GetUtcNow());
        snapshot.MatchedCount.ShouldBe(2);
        snapshot.CurrentRate.ShouldBe(0.2);
    }

    [Fact]
    public async Task Projection_TreatsNullFilterAsMatchAll()
    {
        await using var node = new EventProjectionNode(new EventProjectionOptions
        {
            Filter = null!
        });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            new DateTimeOffset(2026, 6, 3, 11, 30, 0, TimeSpan.Zero),
            "operation.completed")));

        var snapshot = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        snapshot.MatchedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Projection_EmitsEventCarryingCorrelationIdForMatches()
    {
        await using var node = new EventProjectionNode();
        Sink(node.Output);
        var events = Sink(node.Events);
        var timestamp = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);

        var sent = FlowMessage.Create(CreateEvent(timestamp, "first"));
        await node.Input.SendAsync(sent);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(ProjectionDiagnosticNames.ProjectionUpdated);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(sent.CorrelationId);
        @event.Attributes["matchedCount"].ShouldBe(1L);
    }

    [Fact]
    public void Projection_RejectsInvalidOptions()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new EventProjectionNode(new EventProjectionOptions { RateWindowSeconds = 0 }));

        exception.Message.ShouldContain("rateWindowSeconds");
    }

    [Fact]
    public void EventFilterMatcher_MatchesAttributeAndPrefixes()
    {
        var flowEvent = CreateEvent(
            new DateTimeOffset(2026, 6, 3, 13, 0, 0, TimeSpan.Zero),
            "file.created",
            subject: "files/inbox/report.json",
            channel: "events/files",
            attributes: new Dictionary<string, string> { ["kind"] = "document" });

        EventFilterMatcher.IsMatch(
            flowEvent,
            new EventFilter
            {
                TypePrefix = "file.",
                SubjectPrefix = "files/inbox",
                ChannelPrefix = "events/",
                Attributes = new Dictionary<string, string> { ["kind"] = "document" }
            }).ShouldBeTrue();
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private static ProjectionEvent CreateEvent(
        DateTimeOffset timestamp,
        string type,
        string source = "processor",
        string? subject = null,
        string? status = null,
        string? channel = null,
        string? payloadPreview = null,
        string? sourceNodeId = null,
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
