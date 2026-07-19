using System.Numerics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Projections.Tests;

public sealed class FlowEventProjectionNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Matching_events_emit_ordered_snapshots_with_message_lineage()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-19T16:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        await using var node = new FlowEventProjectionNode(
            new EventProjectionOptions
            {
                Name = "orders",
                RateWindowSeconds = 10,
                MaxPreviewChars = 4,
                Filter = new EventFilter { Status = "failed" }
            },
            clock);
        var results = Link(node.Output);
        var events = Link(node.Events);
        var first = FlowMessage.Create(
            Event(timestamp.AddSeconds(-5), "first", status: "failed", preview: "abcdef"),
            new CorrelationId("first")) with
        {
            Headers = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
            {
                ["tenant"] = FlowValue.From("north")
            }
        };
        var ignored = FlowMessage.Create(Event(timestamp.AddSeconds(-2), "ignored", status: "ok"));
        var second = FlowMessage.Create(
            Event(timestamp.AddSeconds(-1), "second", status: "failed"),
            new CorrelationId("second"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(ignored);
        await node.Input.SendAsync(second);

        var firstResult = await results.ReceiveAsync().WaitAsync(Timeout);
        var secondResult = await results.ReceiveAsync().WaitAsync(Timeout);

        firstResult.Payload.Kind.ShouldBe(ProjectionResultKinds.Snapshot);
        firstResult.CorrelationId.ShouldBe(first.CorrelationId);
        firstResult.TraceId.ShouldBe(first.TraceId);
        firstResult.CausationId.ShouldBe(first.MessageId);
        firstResult.Headers.ShouldBeSameAs(first.Headers);
        var firstSnapshot = firstResult.Payload.Value.ShouldNotBeNull();
        firstSnapshot.Name.ShouldBe("orders");
        firstSnapshot.ObservedCount.ShouldBe(1);
        firstSnapshot.MatchedCount.ShouldBe(1);
        firstSnapshot.Latest.ShouldNotBeNull().PayloadPreview.ShouldBe("abcd");

        secondResult.CorrelationId.ShouldBe(second.CorrelationId);
        var secondSnapshot = secondResult.Payload.Value.ShouldNotBeNull();
        secondSnapshot.ObservedCount.ShouldBe(3);
        secondSnapshot.MatchedCount.ShouldBe(2);
        secondSnapshot.CurrentRate.ShouldBe(0.2d);

        var diagnostic = await events.ReceiveAsync().WaitAsync(Timeout);
        diagnostic.Name.ShouldBe(ProjectionDiagnosticNames.ProjectionUpdated);
        diagnostic.Attributes["resultKind"].ShouldBe(ProjectionResultKinds.Snapshot);
    }

    [Fact]
    public async Task Missing_event_is_normal_failure_and_later_input_continues()
    {
        await using var node = new FlowEventProjectionNode();
        var results = Link(node.Output);
        var missing = FlowMessage.Create<ProjectionEvent>(
            null!,
            new CorrelationId("missing"));
        var valid = FlowMessage.Create(
            Event(DateTimeOffset.Parse("2026-07-19T16:05:00Z"), "valid"),
            new CorrelationId("valid"));

        await node.Input.SendAsync(missing);
        await node.Input.SendAsync(valid);

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        var success = await results.ReceiveAsync().WaitAsync(Timeout);

        failure.CorrelationId.ShouldBe(missing.CorrelationId);
        failure.Payload.Kind.ShouldBe(ProjectionResultKinds.ProjectionFailed);
        failure.Payload.IsError.ShouldBeTrue();
        failure.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(ProjectionErrorCodeNames.ProjectionFailed);
        failure.Payload.Error.Details.GetObject()["legacyCode"].GetInteger()
            .ShouldBe(new BigInteger(ProjectionsErrorCodes.ProjectionFailed));
        success.CorrelationId.ShouldBe(valid.CorrelationId);
        success.Payload.Value.ShouldNotBeNull().MatchedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Completion_emits_one_final_snapshot_after_accepted_input()
    {
        var eventTime = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var clock = new FakeTimeProvider(eventTime.AddDays(30));
        await using var node = new FlowEventProjectionNode(
            new EventProjectionOptions
            {
                EmitEveryMatch = false,
                EmitFinalSnapshot = true,
                RateWindowSeconds = 10
            },
            clock);
        var results = Link(node.Output);
        var first = FlowMessage.Create(Event(eventTime, "first"));
        var last = FlowMessage.Create(
            Event(eventTime.AddSeconds(1), "last"),
            new CorrelationId("last"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(last);
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        var result = await results.ReceiveAsync().WaitAsync(Timeout);
        result.Payload.Kind.ShouldBe(ProjectionResultKinds.FinalSnapshot);
        result.CorrelationId.ShouldBe(last.CorrelationId);
        result.TraceId.ShouldBe(last.TraceId);
        result.CausationId.ShouldBe(last.MessageId);
        var snapshot = result.Payload.Value.ShouldNotBeNull();
        snapshot.Timestamp.ShouldBe(clock.GetUtcNow());
        snapshot.ObservedCount.ShouldBe(2);
        snapshot.MatchedCount.ShouldBe(2);
        snapshot.CurrentRate.ShouldBe(0.2d);
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Filtered_input_completes_without_output_when_final_snapshot_is_disabled()
    {
        await using var node = new FlowEventProjectionNode(new EventProjectionOptions
        {
            Filter = new EventFilter { Type = "expected" }
        });
        var results = Link(node.Output);

        await node.Input.SendAsync(EventMessage("ignored"));
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Output_fans_out_each_result_to_every_consumer()
    {
        await using var node = new FlowEventProjectionNode();
        var first = Link(node.Output);
        var second = Link(node.Output);

        await node.Input.SendAsync(EventMessage("event"));

        (await first.ReceiveAsync().WaitAsync(Timeout)).Payload.Value
            .ShouldNotBeNull().MatchedCount.ShouldBe(1);
        (await second.ReceiveAsync().WaitAsync(Timeout)).Payload.Value
            .ShouldNotBeNull().MatchedCount.ShouldBe(1);
    }

    [Fact]
    public void Invalid_options_fail_before_processing()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowEventProjectionNode(new EventProjectionOptions
            {
                RateWindowSeconds = 0
            })).Message.ShouldContain("rateWindowSeconds");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowEventProjectionNode(new EventProjectionOptions
            {
                BoundedCapacity = 0
            })).Message.ShouldContain("boundedCapacity");
    }

    private static FlowMessage<ProjectionEvent> EventMessage(string type)
        => FlowMessage.Create(Event(DateTimeOffset.UtcNow, type));

    private static ProjectionEvent Event(
        DateTimeOffset timestamp,
        string type,
        string status = "ok",
        string? preview = null)
        => new()
        {
            Timestamp = timestamp,
            Type = type,
            Source = "test",
            Status = status,
            PayloadPreview = preview,
            PayloadBytes = preview?.Length
        };

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }
}
