using FluxFlow.Components.Sessions;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the recorder/query carry the correlation id forward, the
// replay source mints a fresh one per record. Timing is driven by an injected
// FakeTimeProvider so replay pacing tests never sleep on the wall clock.
public sealed class SessionComponentTests
{
    // ---- Recorder ------------------------------------------------------------------

    [Fact]
    public async Task Recorder_WritesMessagesInOrderAndPreservesCorrelation()
    {
        var store = new TestSessionStore();
        var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1", Name = "sample", BoundedCapacity = 4 },
            store);
        var output = Sink(node.Output);
        var firstTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var first = FlowMessage.Create(new SessionRecordInput
        {
            Timestamp = firstTimestamp,
            Name = "first",
            Payload = "a"
        });
        var second = FlowMessage.Create(new SessionRecordInput
        {
            Timestamp = firstTimestamp.AddSeconds(1),
            Name = "second",
            Payload = "b"
        });
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);
        node.Complete();

        var firstRecord = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var secondRecord = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        // The session closes when the node is disposed (after the pump has drained).
        await node.DisposeAsync();

        firstRecord.Payload.Sequence.ShouldBe(1);
        firstRecord.CorrelationId.ShouldBe(first.CorrelationId);
        secondRecord.Payload.Sequence.ShouldBe(2);
        secondRecord.CorrelationId.ShouldBe(second.CorrelationId);
        store.Records.Select(record => record.Name).ShouldBe(["first", "second"]);
        store.Metadata.ShouldNotBeNull();
        store.Metadata.SessionId.ShouldBe("session-1");
        store.Metadata.MessageCount.ShouldBe(2);
        store.Metadata.EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Recorder_ReportsAppendFailureWithCorrelationAndContinues()
    {
        var store = new TestSessionStore { FailNextAppend = true };
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new SessionRecordInput { Name = "bad" });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new SessionRecordInput { Name = "good" }));
        node.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var recorded = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        error.Code.ShouldBe(SessionsErrorCodes.RecorderFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        recorded.Payload.Sequence.ShouldBe(1);
        recorded.Payload.Name.ShouldBe("good");
        store.Records.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Recorder_ContinuesFromExistingMessageCountAndCopiesNullAttributes()
    {
        var store = new TestSessionStore { InitialMessageCount = 5 };
        var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new SessionRecordInput
        {
            Name = "next",
            Attributes = null!
        }));
        node.Complete();

        var recorded = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        recorded.Payload.Sequence.ShouldBe(6);
        recorded.Payload.Attributes.ShouldBeEmpty();
        store.Metadata.ShouldNotBeNull();
        store.Metadata.MessageCount.ShouldBe(6);
    }

    [Fact]
    public async Task Recorder_UsesConfiguredClockForDefaultTimestamps()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store,
            timeProvider);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new SessionRecordInput { Name = "timed" }));
        node.Complete();

        var record = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        record.Payload.Timestamp.ShouldBe(timestamp);
        store.Metadata.ShouldNotBeNull();
        store.Metadata.StartedAt.ShouldBe(timestamp);
        store.Metadata.EndedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Recorder_EmitsStartedAndRecordedEventsAndSignalsSessionCompleted()
    {
        var store = new TestSessionStore();
        var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var events = Sink(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<SessionRecord>>());

        await node.Input.SendAsync(FlowMessage.Create(new SessionRecordInput { Name = "only" }));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        // Started/Recorded surface on the Events stream while the ports are open;
        // the session-close diagnostic surfaces on SessionCompleted at disposal because
        // the broadcast ports are already completed by then.
        var names = (await DrainUntilCompletedAsync(events)).Select(@event => @event.Name).ToArray();
        names.ShouldContain(SessionsDiagnosticNames.RecorderStarted);
        names.ShouldContain(SessionsDiagnosticNames.RecorderRecorded);

        await node.DisposeAsync();
        await node.SessionCompleted.WaitAsync(TimeSpan.FromSeconds(30));
        store.Metadata.ShouldNotBeNull();
        store.Metadata.EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Recorder_RequiresStore()
        => Should.Throw<ArgumentNullException>(
            () => new SessionRecorderNode(new SessionRecorderOptions(), null!));

    [Fact]
    public void Recorder_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionRecorderNode(
                new SessionRecorderOptions { BoundedCapacity = 0 },
                new TestSessionStore()));

    // ---- Replay --------------------------------------------------------------------

    [Fact]
    public async Task Replay_EmitsMessagesInOrderAndMintsCorrelation()
    {
        var store = CreateStoreWithRecords();
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1", Mode = SessionReplayMode.Instant },
            store);
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var records = await DrainUntilCompletedAsync(output);
        records.Select(record => record.Payload.Sequence).ShouldBe([1, 2, 3]);
        // The source mints a non-empty correlation id for each emitted record.
        records.ShouldAllBe(record => !record.CorrelationId.IsEmpty);
        records.Select(record => record.CorrelationId).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Replay_SupportsFixedInterval()
    {
        var timeProvider = new TrackingFakeTimeProvider();
        var store = CreateStoreWithRecords(count: 3);
        await using var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.FixedInterval,
                FixedIntervalMilliseconds = 40
            },
            store,
            timeProvider);
        var output = Sink(node.Output);

        await node.StartAsync();

        // The first record emits immediately; the next two each wait for a 40ms
        // FakeTimeProvider delay that only fires once time is advanced.
        await AdvanceUntilCompletedAsync(timeProvider, node, TimeSpan.FromMilliseconds(40));

        var records = await DrainUntilCompletedAsync(output);
        records.Select(record => record.Payload.Sequence).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Replay_SupportsCancellation()
    {
        var store = CreateStoreWithRecords(count: 3);
        await using var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.FixedInterval,
                FixedIntervalMilliseconds = 500
            },
            store);
        var output = Sink(node.Output);

        await node.StartAsync();
        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        first.Payload.Sequence.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_StopsPromptlyWhenCompletedBeforeStart()
    {
        var timeProvider = new FakeTimeProvider();
        var store = CreateStoreWithRecords(count: 3);
        var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.FixedInterval,
                FixedIntervalMilliseconds = 10
            },
            store,
            timeProvider);

        // Completing the source before it starts cancels the run token, so the read
        // loop stops at its first cancellation check instead of awaiting any
        // inter-record delay. Time is never advanced, so if the loop reached a
        // Task.Delay it would hang and disposal would time out; completing within the
        // timeout proves the loop broke before awaiting an inter-record delay.
        node.Complete();
        await node.StartAsync();

        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_UsesMultiplierTiming()
    {
        var timeProvider = new TrackingFakeTimeProvider();
        var store = CreateStoreWithRecords(count: 2, step: TimeSpan.FromMilliseconds(80));
        await using var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.Multiplier,
                SpeedMultiplier = 4
            },
            store,
            timeProvider);
        var output = Sink(node.Output);

        await node.StartAsync();

        // 80ms stored gap divided by the 4x multiplier yields a single 20ms
        // FakeTimeProvider delay before the second record; advance to fire it.
        await AdvanceUntilCompletedAsync(timeProvider, node, TimeSpan.FromMilliseconds(20));

        (await DrainUntilCompletedAsync(output)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Replay_EmitsEvents()
    {
        var store = CreateStoreWithRecords(count: 1);
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1", Mode = SessionReplayMode.Instant },
            store);
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        await DrainUntilCompletedAsync(output);
        var names = (await DrainUntilCompletedAsync(events)).Select(@event => @event.Name).ToArray();
        names.ShouldContain(SessionsDiagnosticNames.ReplayStarted);
        names.ShouldContain(SessionsDiagnosticNames.ReplayEmitted);
        names.ShouldContain(SessionsDiagnosticNames.ReplayCompleted);
    }

    [Fact]
    public async Task Replay_FaultsAndReportsWhenSessionIsMissing()
    {
        var store = new TestSessionStore();
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "missing" },
            store);
        // Don't propagate completion: the source faults its error port right after
        // posting the error, and the buffered error must still be observable.
        var errors = new BufferBlock<FlowError>();
        node.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = false });

        await node.StartAsync();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(SessionsErrorCodes.InvalidSession);
        error.Message.ShouldContain("missing");

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => node.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
        exception.Message.ShouldContain("missing");
    }

    [Fact]
    public void Replay_RejectsMissingSessionId()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new SessionReplayNode(new SessionReplayOptions(), new TestSessionStore()));

        exception.Message.ShouldContain("session id");
    }

    [Fact]
    public void Replay_RequiresStore()
        => Should.Throw<ArgumentNullException>(
            () => new SessionReplayNode(
                new SessionReplayOptions { SessionId = "session-1" },
                null!));

    // ---- Query ---------------------------------------------------------------------

    [Fact]
    public async Task Query_EmitsResultAndSessionOutputsPreservingCorrelation()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 13, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "alpha-one",
            StartedAt = timestamp.AddMinutes(-5),
            Tags = new Dictionary<string, string>(StringComparer.Ordinal) { ["kind"] = "demo" }
        });
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-2",
            Name = "beta-one",
            StartedAt = timestamp.AddMinutes(-3),
            EndedAt = timestamp.AddMinutes(-1),
            Tags = new Dictionary<string, string>(StringComparer.Ordinal) { ["kind"] = "demo" }
        });
        await using var node = new SessionQueryNode(
            new SessionQueryOptions
            {
                NamePrefix = "alpha",
                Tags = new Dictionary<string, string> { ["kind"] = "demo" }
            },
            store,
            timeProvider);
        var output = Sink(node.Output);
        var sessions = Sink(node.Sessions);

        var message = FlowMessage.Create(new SessionQueryRequest { CorrelationId = "corr-1" });
        await node.Input.SendAsync(message);
        node.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var session = await sessions.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.Payload.Timestamp.ShouldBe(timestamp);
        result.Payload.Count.ShouldBe(1);
        result.Payload.CorrelationId.ShouldBe("corr-1");
        result.Payload.Sessions.ShouldHaveSingleItem().SessionId.ShouldBe("session-1");
        session.CorrelationId.ShouldBe(message.CorrelationId);
        session.Payload.SessionId.ShouldBe("session-1");
        store.LastQuery.ShouldNotBeNull();
        store.LastQuery.NamePrefix.ShouldBe("alpha");
        store.LastQuery.Tags["kind"].ShouldBe("demo");
        store.LastQuery.IncludeActive.ShouldBe(true);
        store.LastQuery.IncludeCompleted.ShouldBe(true);
    }

    [Fact]
    public async Task Query_ReportsInvalidRequestWithCorrelationAndContinues()
    {
        var store = new TestSessionStore();
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        });
        await using var node = new SessionQueryNode(new SessionQueryOptions(), store);
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new SessionQueryRequest { Limit = 0 });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        node.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        error.Code.ShouldBe(SessionsErrorCodes.InvalidQuery);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        result.Payload.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Query_ReportsStoreFailureAndContinues()
    {
        var store = new TestSessionStore { FailNextQuery = true };
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        });
        await using var node = new SessionQueryNode(new SessionQueryOptions(), store);
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        node.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        error.Code.ShouldBe(SessionsErrorCodes.QueryFailed);
        result.Payload.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Query_EmitsStartedEventAtConstruction()
    {
        var store = new TestSessionStore();
        await using var node = new SessionQueryNode(new SessionQueryOptions(), store);
        var events = Sink(node.Events);

        var started = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        started.Name.ShouldBe(SessionsDiagnosticNames.QueryStarted);
    }

    [Fact]
    public void Query_RejectsEmptyInclusion()
        => Should.Throw<ArgumentException>(
            () => new SessionQueryNode(
                new SessionQueryOptions { IncludeActive = false, IncludeCompleted = false },
                new TestSessionStore()));

    [Fact]
    public void Query_RequiresStore()
        => Should.Throw<ArgumentNullException>(
            () => new SessionQueryNode(new SessionQueryOptions(), null!));

    // ---- Helpers -------------------------------------------------------------------

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static async Task<IReadOnlyList<T>> DrainUntilCompletedAsync<T>(BufferBlock<T> output)
    {
        var items = new List<T>();
        while (await output.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(30)))
        {
            while (output.TryReceive(out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static TestSessionStore CreateStoreWithRecords(int count = 3, TimeSpan? step = null)
    {
        var store = new TestSessionStore();
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var interval = step ?? TimeSpan.FromSeconds(1);
        store.Metadata = new SessionMetadata
        {
            SessionId = "session-1",
            Name = "seed",
            StartedAt = start,
            MessageCount = count
        };

        for (var index = 0; index < count; index++)
        {
            store.Records.Add(new SessionRecord
            {
                SessionId = "session-1",
                Sequence = index + 1,
                Timestamp = start + (interval * index),
                Name = $"record-{index + 1}",
                Payload = index + 1
            });
        }

        return store;
    }

    private sealed class TestSessionStore : ISessionStore
    {
        public SessionMetadata? Metadata { get; set; }
        public List<SessionMetadata> Sessions { get; } = [];
        public List<SessionRecord> Records { get; } = [];
        public long InitialMessageCount { get; set; }
        public bool FailNextAppend { get; set; }
        public bool FailNextQuery { get; set; }
        public SessionQueryRequest? LastQuery { get; private set; }

        public Task<SessionMetadata?> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                Sessions.FirstOrDefault(session => session.SessionId == sessionId)
                ?? (Metadata?.SessionId == sessionId ? Metadata : null));

        public Task<SessionMetadata> StartSessionAsync(
            SessionStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Metadata = new SessionMetadata
            {
                SessionId = string.IsNullOrWhiteSpace(request.SessionId)
                    ? "generated-session"
                    : request.SessionId,
                Name = request.Name,
                StartedAt = request.StartedAt,
                MessageCount = InitialMessageCount,
                Notes = request.Notes,
                Tags = request.Tags is null
                    ? []
                    : new Dictionary<string, string>(request.Tags, StringComparer.Ordinal)
            };
            UpsertSession(Metadata);
            return Task.FromResult(Metadata);
        }

        public Task<SessionRecord> AppendMessageAsync(
            SessionAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailNextAppend)
            {
                FailNextAppend = false;
                throw new InvalidOperationException("append failed");
            }

            var record = new SessionRecord
            {
                SessionId = request.Session.SessionId,
                Sequence = request.Sequence,
                Timestamp = request.Timestamp,
                Type = request.Input.Type,
                Name = request.Input.Name,
                Payload = request.Input.Payload,
                ContentType = request.Input.ContentType,
                Attributes = request.Input.Attributes is null
                    ? []
                    : new Dictionary<string, string>(request.Input.Attributes, StringComparer.Ordinal)
            };
            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<SessionMetadata> CompleteSessionAsync(
            SessionCompleteRequest request,
            CancellationToken cancellationToken = default)
        {
            Metadata = request.Session with
            {
                EndedAt = request.EndedAt,
                MessageCount = request.MessageCount
            };
            UpsertSession(Metadata);
            return Task.FromResult(Metadata);
        }

        public Task<IReadOnlyList<SessionMetadata>> QuerySessionsAsync(
            SessionQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailNextQuery)
            {
                FailNextQuery = false;
                throw new InvalidOperationException("query failed");
            }

            LastQuery = request;
            IEnumerable<SessionMetadata> query = Sessions.Count > 0
                ? Sessions
                : Metadata is null
                    ? []
                    : [Metadata];
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                query = query.Where(session => StringComparer.Ordinal.Equals(session.Name, request.Name));
            }

            if (!string.IsNullOrWhiteSpace(request.NamePrefix))
            {
                query = query.Where(session =>
                    session.Name?.StartsWith(request.NamePrefix, StringComparison.Ordinal) == true);
            }

            if (request.StartedFrom.HasValue)
            {
                query = query.Where(session => session.StartedAt >= request.StartedFrom.Value);
            }

            if (request.StartedTo.HasValue)
            {
                query = query.Where(session => session.StartedAt <= request.StartedTo.Value);
            }

            if (request.EndedFrom.HasValue)
            {
                query = query.Where(session => session.EndedAt >= request.EndedFrom.Value);
            }

            if (request.EndedTo.HasValue)
            {
                query = query.Where(session => session.EndedAt <= request.EndedTo.Value);
            }

            if (request.IncludeActive == false)
            {
                query = query.Where(session => session.EndedAt is not null);
            }

            if (request.IncludeCompleted == false)
            {
                query = query.Where(session => session.EndedAt is null);
            }

            foreach (var (key, value) in request.Tags)
            {
                query = query.Where(session =>
                    session.Tags.TryGetValue(key, out var actual) &&
                    StringComparer.Ordinal.Equals(actual, value));
            }

            var sessions = query
                .OrderBy(session => session.StartedAt)
                .Take(request.Limit ?? int.MaxValue)
                .Select(CopySession)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SessionMetadata>>(sessions);
        }

        public async IAsyncEnumerable<SessionRecord> ReadMessagesAsync(
            SessionReadRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IEnumerable<SessionRecord> query = Records
                .Where(record => record.SessionId == request.SessionId)
                .OrderBy(record => record.Sequence);
            if (request.StartSequence.HasValue)
            {
                query = query.Where(record => record.Sequence >= request.StartSequence.Value);
            }

            if (request.MaxMessages.HasValue)
            {
                query = query.Take(request.MaxMessages.Value);
            }

            foreach (var record in query)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return record;
            }
        }

        private void UpsertSession(SessionMetadata session)
        {
            var index = Sessions.FindIndex(existing => existing.SessionId == session.SessionId);
            if (index < 0)
            {
                Sessions.Add(CopySession(session));
                return;
            }

            Sessions[index] = CopySession(session);
        }

        private static SessionMetadata CopySession(SessionMetadata session)
            => session with
            {
                Tags = session.Tags is null
                    ? []
                    : new Dictionary<string, string>(session.Tags, StringComparer.Ordinal)
            };
    }

    // Drives a FakeTimeProvider forward until the node finishes. The replay loop arms
    // each inter-record Task.Delay lazily (only after the previous record is sent), so a
    // single large Advance cannot fire delays that are not armed yet. This drains them
    // deterministically: advance exactly once for each newly created timer, gating on
    // the wrapper's created count (and its registration signal) rather than polling with
    // sleeps. Counting created timers avoids the lost-wakeup where a delay is armed
    // before the test looks.
    private static async Task AdvanceUntilCompletedAsync(
        TrackingFakeTimeProvider timeProvider,
        IFlowNode node,
        TimeSpan step)
    {
        var fired = 0;
        while (!node.Completion.IsCompleted)
        {
            // Capture the next-timer registration signal BEFORE reading the count, so a
            // timer armed in the gap between the count check and the await is not a
            // lost-wakeup: either the count check below sees it, or this captured signal
            // completes when it is armed.
            var scheduled = timeProvider.TimerScheduled;

            if (timeProvider.CreatedTimerCount > fired)
            {
                // An inter-record delay is armed (or already was) but not yet released.
                timeProvider.Advance(step);
                fired++;
                continue;
            }

            // No unfired timer yet: wait until the loop arms the next one or completes.
            await Task.WhenAny(scheduled, node.Completion)
                .WaitAsync(TimeSpan.FromSeconds(30));
        }

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
