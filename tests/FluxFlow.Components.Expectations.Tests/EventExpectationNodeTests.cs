using System.Collections;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Expectations.Tests;

public sealed class EventExpectationNodeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Expect_match_is_normal_result_with_message_lineage()
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:00:00Z");
        var clock = new FakeTimeProvider(now);
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Name = "order-completed",
                Filter = new EventFilter { Type = "order.completed" }
            },
            clock);
        var results = Link(node.Output);
        var events = Link(node.Events);
        var input = FlowMessage.Create(
            CreateEvent(now, "order.completed"),
            new CorrelationId("order-42"),
            new TraceId("trace-42"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenant"] = "north"
            });

        (await node.Input.SendAsync(input).WaitAsync(WaitTimeout)).ShouldBeTrue();

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeFalse();
        output.Value.Kind.ShouldBe(EventExpectationResultKind.Expect);
        output.Value.Satisfied.ShouldBeTrue();
        output.Value.Matched.ShouldBeTrue();
        output.Value.Name.ShouldBe("order-completed");
        output.CorrelationId.ShouldBe(input.CorrelationId);
        output.TraceId.ShouldBe(input.TraceId);
        output.CausationId.ShouldBe(input.MessageId);
        output.MessageId.ShouldNotBe(input.MessageId);
        output.Headers.ShouldBeSameAs(input.Headers);

        var diagnostic = await events.ReceiveAsync().WaitAsync(WaitTimeout);
        diagnostic.Name.ShouldBe(ExpectationDiagnosticNames.Matched);
        diagnostic.CorrelationId.ShouldBe(input.CorrelationId);
        diagnostic.Attributes["resultKind"].ShouldBe(ExpectationResultKinds.Matched);
        diagnostic.Attributes["isError"].ShouldBe(false);
    }

    [Fact]
    public async Task Guard_match_is_normal_unmet_result()
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:05:00Z");
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Guard,
                Filter = new EventFilter { Status = "failed" }
            },
            new FakeTimeProvider(now));
        var results = Link(node.Output);

        await node.Input.SendAsync(CreateMessage(now, status: "failed"));

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeFalse();
        output.Value.Satisfied.ShouldBeFalse();
        output.Value.Matched.ShouldBeTrue();
        output.Value.Kind.ShouldBe(EventExpectationResultKind.Guard);
    }

    [Theory]
    [InlineData(EventExpectationNodeKind.Expect, false)]
    [InlineData(EventExpectationNodeKind.Guard, true)]
    public async Task Timeout_is_normal_result(
        EventExpectationNodeKind kind,
        bool satisfied)
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:10:00Z");
        var clock = new FakeTimeProvider(now);
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = kind,
                TimeoutMilliseconds = 250,
                Filter = new EventFilter { Type = "never" }
            },
            clock);
        var results = Link(node.Output);

        clock.Advance(TimeSpan.FromMilliseconds(250));

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeFalse();
        output.Value.Satisfied.ShouldBe(satisfied);
        output.Value.TimedOut.ShouldBeTrue();
        output.Value.EvaluatedAt.ShouldBe(clock.GetUtcNow());
    }

    [Theory]
    [InlineData(EventExpectationNodeKind.Expect, false)]
    [InlineData(EventExpectationNodeKind.Guard, true)]
    public async Task Completion_drains_input_then_emits_one_normal_result(
        EventExpectationNodeKind kind,
        bool satisfied)
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:15:00Z");
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = kind,
                Filter = new EventFilter { Type = "match" }
            },
            new FakeTimeProvider(now));
        var results = Link(node.Output);
        var input = CreateMessage(now, type: "ignored");

        (await node.Input.SendAsync(input).WaitAsync(WaitTimeout)).ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(WaitTimeout);

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeFalse();
        output.Value.Satisfied.ShouldBe(satisfied);
        output.Value.TimedOut.ShouldBeFalse();
        output.Value.ObservedEvents.Count.ShouldBe(1);
        output.CorrelationId.ShouldBe(input.CorrelationId);
        output.CausationId.ShouldBe(input.MessageId);
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Evaluation_failure_is_one_normal_error_result()
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:20:00Z");
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                TimeoutMilliseconds = 100,
                Filter = new EventFilter
                {
                    Attributes = new Dictionary<string, string> { ["tenant"] = "north" }
                }
            },
            new FakeTimeProvider(now));
        var results = Link(node.Output);
        var events = Link(node.Events);
        var bad = FlowMessage.Create(
            CreateEvent(now, "job.finished", new ThrowingDictionary()),
            new CorrelationId("bad"));

        (await node.Input.SendAsync(bad).WaitAsync(WaitTimeout)).ShouldBeTrue();

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeTrue();
        output.Error.ShouldNotBeNull().Code
            .ShouldBe(ExpectationErrorCodeNames.EvaluationFailed);
        output.Error.Category.ShouldBe("Expectations");
        output.CorrelationId.ShouldBe(bad.CorrelationId);
        output.CausationId.ShouldBe(bad.MessageId);

        var diagnostic = await events.ReceiveAsync().WaitAsync(WaitTimeout);
        diagnostic.Name.ShouldBe(ExpectationDiagnosticNames.EvaluationFailed);
        diagnostic.Attributes["isError"].ShouldBe(true);

        node.Complete();
        await node.Completion.WaitAsync(WaitTimeout);
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Match_wins_once_over_later_timeout_and_completion()
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:25:00Z");
        var clock = new FakeTimeProvider(now);
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                TimeoutMilliseconds = 100,
                Filter = new EventFilter { Type = "match" }
            },
            clock);
        var results = Link(node.Output);

        await node.Input.SendAsync(CreateMessage(now, type: "match"));
        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.Value.Satisfied.ShouldBeTrue();
        output.Value.Matched.ShouldBeTrue();

        clock.Advance(TimeSpan.FromMilliseconds(100));
        await node.CompleteWithResultAsync().WaitAsync(WaitTimeout);
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Output_fans_out_one_result_to_every_consumer()
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:30:00Z");
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Filter = new EventFilter { Type = "match" }
            },
            new FakeTimeProvider(now));
        var first = Link(node.Output);
        var second = Link(node.Output);

        await node.Input.SendAsync(CreateMessage(now, type: "match"));

        (await first.ReceiveAsync().WaitAsync(WaitTimeout))
            .Value.Satisfied.ShouldBeTrue();
        (await second.ReceiveAsync().WaitAsync(WaitTimeout))
            .Value.Satisfied.ShouldBeTrue();
    }

    [Fact]
    public async Task Result_caps_observed_events_and_previews_and_snapshots_filter()
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:35:00Z");
        var requiredAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = "north"
        };
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Filter = new EventFilter
                {
                    Type = "match",
                    Attributes = requiredAttributes
                },
                MaxObservedEvents = 2,
                MaxPreviewChars = 4
            },
            new FakeTimeProvider(now));
        var results = Link(node.Output);
        requiredAttributes.Clear();
        var eventAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = "north"
        };

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            now,
            "ignored-1",
            eventAttributes,
            payloadPreview: "first")));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            now,
            "ignored-2",
            eventAttributes,
            payloadPreview: "second")));
        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            now,
            "match",
            eventAttributes,
            payloadPreview: "abcdef")));

        var value = (await results.ReceiveAsync().WaitAsync(WaitTimeout))
            .Value;
        value.ObservedEvents.Select(@event => @event.Type)
            .ShouldBe(["ignored-2", "match"], ignoreOrder: false);
        value.ObservedEvents.Select(@event => @event.PayloadPreview)
            .ShouldBe(["seco", "abcd"], ignoreOrder: false);
        value.MatchedEvent.ShouldNotBeNull().PayloadPreview.ShouldBe("abcd");
        value.Filter.Attributes["tenant"].ShouldBe("north");
    }

    [Fact]
    public async Task Concurrent_timeout_and_completion_emit_exactly_one_result()
    {
        var now = DateTimeOffset.Parse("2026-07-19T10:40:00Z");
        var clock = new FakeTimeProvider(now);
        await using var node = new EventExpectationNode(
            new EventExpectationOptions { TimeoutMilliseconds = 100 },
            clock);
        var results = Link(node.Output);
        using var barrier = new Barrier(2);

        var timeout = Task.Run(() =>
        {
            barrier.SignalAndWait();
            clock.Advance(TimeSpan.FromMilliseconds(100));
        });
        var completion = Task.Run(() =>
        {
            barrier.SignalAndWait();
            node.Complete();
        });

        await Task.WhenAll(timeout, completion).WaitAsync(WaitTimeout);
        await node.Completion.WaitAsync(WaitTimeout);
        var result = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        result.Value.Reason.ShouldBeOneOf(
            "Expected event was not observed before timeout.",
            "Input completed before a matching event was observed.");
        results.TryReceive(out _).ShouldBeFalse();
        await results.Completion.WaitAsync(WaitTimeout);
        results.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0d, 10, 256, 128)]
    [InlineData(null, -1, 256, 128)]
    [InlineData(null, 10, -1, 128)]
    [InlineData(null, 10, 256, 0)]
    public void Invalid_options_are_rejected(
        double? timeoutMilliseconds,
        int maxObservedEvents,
        int maxPreviewChars,
        int boundedCapacity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EventExpectationNode(
            new EventExpectationOptions
            {
                TimeoutMilliseconds = timeoutMilliseconds,
                MaxObservedEvents = maxObservedEvents,
                MaxPreviewChars = maxPreviewChars,
                BoundedCapacity = boundedCapacity
            }));
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static FlowMessage<ProjectionEvent> CreateMessage(
        DateTimeOffset timestamp,
        string type = "event",
        string? status = null)
        => FlowMessage.Create(CreateEvent(timestamp, type, status: status));

    private static ProjectionEvent CreateEvent(
        DateTimeOffset timestamp,
        string type,
        IReadOnlyDictionary<string, string>? attributes = null,
        string? status = null,
        string? payloadPreview = null)
        => new()
        {
            Timestamp = timestamp,
            Type = type,
            Source = "test",
            Status = status,
            PayloadBytes = payloadPreview?.Length,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>()
        };

    private sealed class ThrowingDictionary : IReadOnlyDictionary<string, string>
    {
        public string this[string key] => throw new InvalidOperationException("boom");
        public IEnumerable<string> Keys => throw new InvalidOperationException("boom");
        public IEnumerable<string> Values => throw new InvalidOperationException("boom");
        public int Count => throw new InvalidOperationException("boom");
        public bool ContainsKey(string key) => throw new InvalidOperationException("boom");
        public bool TryGetValue(string key, out string value)
            => throw new InvalidOperationException("boom");
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => throw new InvalidOperationException("boom");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
