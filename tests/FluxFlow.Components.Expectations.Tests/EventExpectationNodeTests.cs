using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Expectations.Tests;

// Every test news the node directly — no engine, no registry. Events travel as
// FlowMessage<ProjectionEvent> envelopes; the correlation id flows event -> result.
// FakeTimeProvider drives the timeout deterministically: the node arms its timer in
// the constructor over the injected TimeProvider, so advancing the clock — never a
// real-time wait — is what fires it.
public sealed class EventExpectationNodeTests
{
    [Fact]
    public async Task Expect_MatchesEventAndEmitsSatisfiedResultPreservingCorrelation()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.Zero));
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Expect,
                Name = "failed-order",
                MaxObservedEvents = 2,
                MaxPreviewChars = 4,
                Filter = new EventFilter
                {
                    Type = "operation.completed",
                    Status = "failed",
                    SubjectPrefix = "orders/"
                }
            },
            timeProvider);
        var results = Sink(node.Output);
        var timestamp = new DateTimeOffset(2026, 6, 3, 7, 59, 0, TimeSpan.Zero);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            timestamp,
            "operation.completed",
            status: "ok",
            subject: "orders/1")));
        var matchSent = FlowMessage.Create(CreateEvent(
            timestamp.AddSeconds(1),
            "operation.completed",
            status: "failed",
            subject: "orders/2",
            payloadPreview: "abcdef"));
        await node.Input.SendAsync(matchSent);

        var received = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        received.CorrelationId.ShouldBe(matchSent.CorrelationId);   // correlation flows event -> result
        var result = received.Payload;
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
    }

    [Fact]
    public async Task Expect_TimesOutWhenMatchIsNotObserved()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 9, 0, 0, TimeSpan.Zero));
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Expect,
                TimeoutMilliseconds = 500,
                Filter = new EventFilter { Type = "job.finished" }
            },
            timeProvider);
        var results = Sink(node.Output);

        // Send a non-matching event, then wait until the node has recorded it so the
        // timeout cannot resolve against an empty observation list. The timeout timer
        // was armed synchronously in the constructor, so advancing the clock fires it.
        await node.Input.SendAsync(FlowMessage.Create(
            CreateEvent(timeProvider.GetUtcNow(), "job.started")));
        await WaitUntilAsync(() => node.ObservedEventCount == 1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));

        var result = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload;
        result.Satisfied.ShouldBeFalse();
        result.Matched.ShouldBeFalse();
        result.TimedOut.ShouldBeTrue();
        result.ObservedEvents.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Guard_SucceedsOnTimeoutWhenNoMatchArrives()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Guard,
                TimeoutMilliseconds = 1000,
                Filter = new EventFilter { Status = "failed" }
            },
            timeProvider);
        var results = Sink(node.Output);

        // The timer is armed in the constructor; advancing the clock past the timeout
        // is all it takes to fire it — no real-time wait, fully deterministic.
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var result = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload;
        result.Kind.ShouldBe(EventExpectationResultKind.Guard);
        result.Satisfied.ShouldBeTrue();
        result.Matched.ShouldBeFalse();
        result.TimedOut.ShouldBeTrue();
    }

    [Fact]
    public async Task Guard_FailsWhenMatchingEventArrives()
    {
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Guard,
                Filter = new EventFilter
                {
                    ChannelPrefix = "events/",
                    Attributes = new Dictionary<string, string> { ["severity"] = "critical" }
                }
            });
        var results = Sink(node.Output);

        var sent = FlowMessage.Create(CreateEvent(
            new DateTimeOffset(2026, 6, 3, 11, 0, 0, TimeSpan.Zero),
            "operation.failed",
            channel: "events/orders",
            attributes: new Dictionary<string, string> { ["severity"] = "critical" }));
        await node.Input.SendAsync(sent);

        var received = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        received.CorrelationId.ShouldBe(sent.CorrelationId);
        var result = received.Payload;
        result.Kind.ShouldBe(EventExpectationResultKind.Guard);
        result.Satisfied.ShouldBeFalse();
        result.Matched.ShouldBeTrue();
        result.TimedOut.ShouldBeFalse();
        result.MatchedEvent.ShouldNotBeNull();
        result.MatchedEvent.Channel.ShouldBe("events/orders");
    }

    [Fact]
    public async Task Expect_EmitsNotMatchedWhenInputCompletes()
    {
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Expect,
                Filter = new EventFilter { TypePrefix = "task." }
            });
        var results = Sink(node.Output);

        // The kit closes the output port as soon as the input drains, so the
        // completion-resolution rides an in-band flush sent through the ordered pump.
        await node.CompleteWithResultAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var result = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload;
        result.Satisfied.ShouldBeFalse();
        result.Matched.ShouldBeFalse();
        result.TimedOut.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CompleteWithResult_ResolvesAsCompletedEvenWithAnArmedTimeout()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero));
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Expect,
                TimeoutMilliseconds = 1000
            },
            timeProvider);
        var results = Sink(node.Output);

        // A timeout is armed but the clock is never advanced, so completion is the
        // first trigger to resolve: a "completed", not "timed out", result.
        await node.CompleteWithResultAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var result = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload;
        result.Satisfied.ShouldBeFalse();
        result.TimedOut.ShouldBeFalse();
        await results.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Output_FansOutTheResultToEveryConsumer()
    {
        // No engine: one node's output linked to two downstream consumers. Both see
        // the single resolved result (a broadcast port).
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Expect,
                Filter = new EventFilter { Type = "job.finished" }
            });
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(CreateEvent(
            new DateTimeOffset(2026, 6, 3, 13, 0, 0, TimeSpan.Zero),
            "job.finished")));

        (await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.Satisfied.ShouldBeTrue();
        (await second.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.Satisfied.ShouldBeTrue();
    }

    [Fact]
    public async Task Match_EmitsMatchedDiagnosticOnEventsPortCarryingCorrelation()
    {
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Expect,
                Filter = new EventFilter { Type = "job.finished" }
            });
        Sink(node.Output);
        var events = Sink(node.Events);

        var sent = FlowMessage.Create(CreateEvent(
            new DateTimeOffset(2026, 6, 3, 14, 0, 0, TimeSpan.Zero),
            "job.finished"));
        await node.Input.SendAsync(sent);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        @event.Name.ShouldBe(ExpectationDiagnosticNames.Matched);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(sent.CorrelationId);
    }

    [Fact]
    public async Task EvaluationFailure_ReportsErrorOnErrorPortAndDoesNotFault()
    {
        // Drive the failure path through a poisoned attribute bag on the event: the
        // node enumerates it while summarizing/matching, and the throw surfaces as a
        // FlowError on the error port instead of faulting the pump.
        await using var node = new EventExpectationNode(
            new EventExpectationOptions
            {
                Kind = EventExpectationNodeKind.Expect,
                Filter = new EventFilter
                {
                    Attributes = new Dictionary<string, string> { ["k"] = "v" }
                }
            });
        var errors = Sink(node.Errors);

        var sent = FlowMessage.Create(new ProjectionEvent
        {
            Timestamp = new DateTimeOffset(2026, 6, 3, 15, 0, 0, TimeSpan.Zero),
            Type = "job.finished",
            Source = "processor",
            Attributes = new ThrowingDictionary()
        });
        await node.Input.SendAsync(sent);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(ExpectationsErrorCodes.EvaluationFailed);
        error.CorrelationId.ShouldBe(sent.CorrelationId);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public void Expectation_RejectsInvalidTimeout()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new EventExpectationNode(new EventExpectationOptions
            {
                TimeoutMilliseconds = 0
            }));

        exception.Message.ShouldContain("timeoutMilliseconds");
    }

    [Fact]
    public void Expectation_RejectsInvalidObservedEventLimit()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new EventExpectationNode(new EventExpectationOptions
            {
                MaxObservedEvents = -1
            }));

        exception.Message.ShouldContain("maxObservedEvents");
    }

    [Fact]
    public void Expectation_RejectsInvalidPreviewLimit()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new EventExpectationNode(new EventExpectationOptions
            {
                MaxPreviewChars = -1
            }));

        exception.Message.ShouldContain("maxPreviewChars");
    }

    [Fact]
    public void Expectation_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new EventExpectationNode(new EventExpectationOptions
            {
                BoundedCapacity = 0
            }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
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
            (Environment.TickCount64 - startedAt).ShouldBeLessThan(30000);
            await Task.Delay(10);
        }
    }

    // An attribute bag that throws when read, forcing the node's catch -> EmitError
    // path (it copies/enumerates the event's attributes while summarizing/matching).
    private sealed class ThrowingDictionary : IReadOnlyDictionary<string, string>
    {
        public string this[string key] => throw new InvalidOperationException("boom");
        public IEnumerable<string> Keys => throw new InvalidOperationException("boom");
        public IEnumerable<string> Values => throw new InvalidOperationException("boom");
        public int Count => throw new InvalidOperationException("boom");
        public bool ContainsKey(string key) => throw new InvalidOperationException("boom");
        public bool TryGetValue(string key, out string value) => throw new InvalidOperationException("boom");
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => throw new InvalidOperationException("boom");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => throw new InvalidOperationException("boom");
    }
}
