using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

public sealed class SessionComponentTests
{
    [Fact]
    public async Task Recorder_passes_typed_content_to_the_store()
    {
        var store = new TestSessionStore();
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(ContentInput(7, "typed")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await DrainUntilCompletedAsync(output);

        store.Records.ShouldHaveSingleItem().Payload
            .ShouldBeOfType<FlowContent>().Bytes.AsSpan().ToArray()
            .ShouldBe(new byte[] { 7 });
    }

    [Fact]
    public async Task Recorder_preserves_content_lineage_order_and_existing_sequence()
    {
        var store = new TestSessionStore
        {
            InitialMessageCount = 5,
            SerializePayloadAsJsonElement = true
        };
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions
            {
                SessionId = "session-1",
                SessionName = "sample",
                BoundedCapacity = 4
            },
            store);
        var output = Sink(node.Output);
        var first = FlowMessage.Create(
            ContentInput(1, "first"),
            headers: new Dictionary<string, string>
            {
                ["tenant"] = "north"
            });
        var second = FlowMessage.Create(ContentInput(2, "second"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);
        node.Complete();

        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await node.SessionCompleted.WaitAsync(TimeSpan.FromSeconds(30));

        results.Count.ShouldBe(2);
        results.Select(result => result.Value.Sequence)
            .ShouldBe([6L, 7L]);
        results.Select(result => result.Value.Name)
            .ShouldBe(["first", "second"]);
        results[0].Value.Content.Bytes.ToArray()
            .ShouldBe(new byte[] { 1 });
        results[0].CorrelationId.ShouldBe(first.CorrelationId);
        results[0].TraceId.ShouldBe(first.TraceId);
        results[0].CausationId.ShouldBe(first.MessageId);
        results[0].Headers["tenant"].ShouldBe("north");
        results[1].CorrelationId.ShouldBe(second.CorrelationId);
        store.Metadata.ShouldNotBeNull().MessageCount.ShouldBe(7);
        store.Metadata.EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Recorder_returns_start_failure_as_data_and_continues()
    {
        var store = new TestSessionStore { ReturnNullStartSessionOnce = true };
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);
        var failed = FlowMessage.Create(ContentInput(1, "failed"));
        var succeeded = FlowMessage.Create(ContentInput(2, "succeeded"));

        await node.Input.SendAsync(failed);
        await node.Input.SendAsync(succeeded);
        node.Complete();

        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results.Count.ShouldBe(2);
        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoreUnavailable);
        results[0].CorrelationId.ShouldBe(failed.CorrelationId);
        results[1].IsError.ShouldBeFalse();
        results[1].Value.Sequence.ShouldBe(1);
        results[1].CorrelationId.ShouldBe(succeeded.CorrelationId);
    }

    [Fact]
    public async Task Recorder_propagates_input_and_append_failures_as_data_and_continues()
    {
        var store = new TestSessionStore { FailNextAppend = true };
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);
        var upstreamError = new FlowError(
            "upstream.failed",
            "Upstream processing failed.",
            "test");
        var unavailable = FlowMessage.CreateError<SessionContentRecordInput>(upstreamError);
        var appendFailure = FlowMessage.Create(ContentInput(1, "append-failure"));
        var succeeded = FlowMessage.Create(ContentInput(2, "succeeded"));

        await node.Input.SendAsync(unavailable);
        await node.Input.SendAsync(appendFailure);
        await node.Input.SendAsync(succeeded);
        node.Complete();

        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results.Count.ShouldBe(3);
        results[0].Error.ShouldBeSameAs(upstreamError);
        results[1].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.RecordFailed);
        results[2].IsError.ShouldBeFalse();
        results[2].Value.Sequence.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Recorder_returns_null_append_result_as_data_and_continues()
    {
        var store = new TestSessionStore { ReturnNullAppendOnce = true };
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(ContentInput(1, "failed")));
        await node.Input.SendAsync(FlowMessage.Create(ContentInput(2, "succeeded")));
        node.Complete();

        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results.Count.ShouldBe(2);
        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoreUnavailable);
        results[0].Error.ShouldNotBeNull().Message.ShouldContain("null record");
        results[1].Value.Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task Recorder_uses_clock_and_emits_lifecycle_events()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store,
            clock);
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(ContentInput(1, "timed")));
        node.Complete();

        var result = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var diagnosticEvents = await DrainUntilCompletedAsync(events);

        result.Value.Timestamp.ShouldBe(timestamp);
        store.Metadata.ShouldNotBeNull().StartedAt.ShouldBe(timestamp);
        store.Metadata.EndedAt.ShouldBe(timestamp);
        diagnosticEvents.Select(@event => @event.Name).ShouldContain(
            SessionsDiagnosticNames.RecorderStarted);
        diagnosticEvents.Select(@event => @event.Name).ShouldContain(
            SessionsDiagnosticNames.RecorderRecorded);
        diagnosticEvents.Select(@event => @event.Name).ShouldContain(
            SessionsDiagnosticNames.RecorderCompleted);
    }

    [Fact]
    public async Task Recorder_reports_close_failure_without_faulting_normal_completion()
    {
        var store = new TestSessionStore { ReturnNullCompleteOnce = true };
        await using var node = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(ContentInput(1, "only")));
        node.Complete();

        await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var exception = await Should.ThrowAsync<Exception>(
            () => node.SessionCompleted.WaitAsync(TimeSpan.FromSeconds(30)));
        var diagnosticEvents = await DrainUntilCompletedAsync(events);

        exception.Message.ShouldContain("failed to complete session");
        node.Completion.IsFaulted.ShouldBeFalse();
        diagnosticEvents.ShouldContain(@event =>
            @event.Name == SessionsDiagnosticNames.RecorderFailed &&
            Equals(@event.Attributes["errorCode"], SessionErrorCodeNames.CompleteFailed));
    }

    [Fact]
    public void Recorder_validates_dependencies_and_capacity()
    {
        Should.Throw<ArgumentNullException>(
            () => new SessionRecorderNode(new SessionRecorderOptions(), null!));
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionRecorderNode(
                new SessionRecorderOptions { BoundedCapacity = 0 },
                new TestSessionStore()));
    }

    [Fact]
    public async Task Replay_emits_exact_content_in_order_and_mints_message_identity()
    {
        var store = CreateStoreWithContentRecords(count: 4);
        await using var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.Instant,
                StartSequence = 2,
                MaxMessages = 2
            },
            store);
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var results = await DrainUntilCompletedAsync(output);

        results.Select(result => result.Value.Sequence)
            .ShouldBe([2L, 3L]);
        results.Select(result => result.Value.Content.Bytes[0])
            .ShouldBe([(byte)2, (byte)3]);
        results.ShouldAllBe(result => !result.IsError);
        results.ShouldAllBe(result => result.CorrelationId == null);
        results.ShouldAllBe(result => !result.TraceId.IsEmpty);
        results.Select(result => result.TraceId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Replay_supports_fixed_interval()
    {
        var clock = new TrackingFakeTimeProvider();
        var store = CreateStoreWithContentRecords(count: 3);
        await using var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.FixedInterval,
                FixedIntervalMilliseconds = 40
            },
            store,
            clock);
        var output = Sink(node.Output);

        await node.StartAsync();
        await AdvanceUntilCompletedAsync(clock, node, TimeSpan.FromMilliseconds(40));

        (await DrainUntilCompletedAsync(output))
            .Select(result => result.Value.Sequence)
            .ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task Replay_supports_multiplier_timing()
    {
        var clock = new TrackingFakeTimeProvider();
        var store = CreateStoreWithContentRecords(
            count: 2,
            step: TimeSpan.FromMilliseconds(80));
        await using var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.Multiplier,
                SpeedMultiplier = 4
            },
            store,
            clock);
        var output = Sink(node.Output);

        await node.StartAsync();
        await AdvanceUntilCompletedAsync(clock, node, TimeSpan.FromMilliseconds(20));

        (await DrainUntilCompletedAsync(output)).Count.ShouldBe(2);
        clock.CreatedTimerCount.ShouldBe(1);
    }

    [Fact]
    public async Task Replay_cancellation_completes_without_late_output()
    {
        var store = CreateStoreWithContentRecords(count: 3);
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

        first.Value.Sequence.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_completed_before_start_stops_promptly()
    {
        var clock = new FakeTimeProvider();
        var node = new SessionReplayNode(
            new SessionReplayOptions
            {
                SessionId = "session-1",
                Mode = SessionReplayMode.FixedInterval,
                FixedIntervalMilliseconds = 10
            },
            CreateStoreWithContentRecords(count: 3),
            clock);

        node.Complete();
        await node.StartAsync();
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_pre_canceled_start_does_not_consume_start_state()
    {
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1" },
            CreateStoreWithContentRecords(count: 1));
        var output = Sink(node.Output);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => node.StartAsync(canceled.Token));
        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Replay_emits_lifecycle_events()
    {
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1" },
            CreateStoreWithContentRecords(count: 1));
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await DrainUntilCompletedAsync(output);
        var diagnosticEvents = await DrainUntilCompletedAsync(events);

        diagnosticEvents.Select(@event => @event.Name).ShouldContain(
            SessionsDiagnosticNames.ReplayStarted);
        diagnosticEvents.Select(@event => @event.Name).ShouldContain(
            SessionsDiagnosticNames.ReplayEmitted);
        diagnosticEvents.Select(@event => @event.Name).ShouldContain(
            SessionsDiagnosticNames.ReplayCompleted);
    }

    [Fact]
    public async Task Replay_returns_missing_session_as_normal_result()
    {
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "missing" },
            new TestSessionStore());
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var result = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();

        result.Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.SessionNotFound);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_returns_mid_stream_store_failure_as_normal_result()
    {
        var store = CreateStoreWithContentRecords(count: 3);
        store.FailReadAfter = 1;
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var results = await DrainUntilCompletedAsync(output);

        results.Count.ShouldBe(2);
        results[0].Value.Sequence.ShouldBe(1);
        results[1].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.ReplayFailed);
        results[1].Error.ShouldNotBeNull().Message.ShouldContain("mid-stream");
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_returns_null_stream_as_normal_result()
    {
        var store = CreateStoreWithContentRecords(count: 1);
        store.ReturnNullReadStreamOnce = true;
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var result = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();

        result.Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoreUnavailable);
        result.Error.ShouldNotBeNull().Message.ShouldContain("null message stream");
    }

    [Fact]
    public async Task Replay_returns_malformed_records_as_data_and_continues()
    {
        var store = CreateStoreWithContentRecords(count: 2);
        store.Records.Insert(1, new SessionRecord
        {
            SessionId = "session-1",
            Sequence = 2,
            Timestamp = store.Records[0].Timestamp.AddMilliseconds(1),
            Payload = "not-an-envelope"
        });
        store.Records[2] = store.Records[2] with { Sequence = 3 };
        store.Metadata = store.Metadata.ShouldNotBeNull() with { MessageCount = 3 };
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var results = await DrainUntilCompletedAsync(output);

        results.Count.ShouldBe(3);
        results[0].IsError.ShouldBeFalse();
        results[1].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoredContentInvalid);
        results[2].Value.Sequence.ShouldBe(3);
    }

    [Fact]
    public async Task Replay_returns_null_record_as_data_and_continues()
    {
        var store = CreateStoreWithContentRecords(count: 2);
        store.ReturnNullReadRecordOnce = true;
        await using var node = new SessionReplayNode(
            new SessionReplayOptions { SessionId = "session-1" },
            store);
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var results = await DrainUntilCompletedAsync(output);

        results.Count.ShouldBe(2);
        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoreUnavailable);
        results[1].Value.Sequence.ShouldBe(2);
    }

    [Fact]
    public void Replay_validates_options_and_dependencies()
    {
        Should.Throw<ArgumentException>(
            () => new SessionReplayNode(new SessionReplayOptions(), new TestSessionStore()));
        Should.Throw<ArgumentNullException>(
            () => new SessionReplayNode(
                new SessionReplayOptions { SessionId = "session-1" },
                null!));
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayNode(
                new SessionReplayOptions { SessionId = "session-1", BoundedCapacity = 0 },
                new TestSessionStore()));
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionReplayNode(
                new SessionReplayOptions { SessionId = "session-1", SpeedMultiplier = 0 },
                new TestSessionStore()));
    }

    [Fact]
    public async Task Query_returns_filtered_result_with_clock_and_lineage()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 13, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "alpha-one",
            StartedAt = timestamp.AddMinutes(-5),
            Tags = new Dictionary<string, string> { ["kind"] = "demo" }
        });
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-2",
            Name = "beta-one",
            StartedAt = timestamp.AddMinutes(-3),
            EndedAt = timestamp.AddMinutes(-1),
            Tags = new Dictionary<string, string> { ["kind"] = "demo" }
        });
        await using var node = new SessionQueryNode(
            new SessionQueryOptions
            {
                NamePrefix = "alpha",
                Tags = new Dictionary<string, string> { ["kind"] = "demo" },
                EmitSessionsInResult = true
            },
            store,
            clock);
        var output = Sink(node.Output);
        var events = Sink(node.Events);
        var input = FlowMessage.Create(new SessionQueryRequest { CorrelationId = "corr-1" });

        await node.Input.SendAsync(input);
        node.Complete();
        var result = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        var diagnostics = await DrainUntilCompletedAsync(events);

        result.IsError.ShouldBeFalse();
        result.Value.Count.ShouldBe(1);
        result.Value.Sessions.ShouldHaveSingleItem()
            .SessionId.ShouldBe("session-1");
        result.CorrelationId.ShouldBe(input.CorrelationId);
        result.CausationId.ShouldBe(input.MessageId);
        diagnostics.Single(@event => @event.Name == SessionsDiagnosticNames.QueryCompleted)
            .Timestamp.ShouldBe(timestamp);
        store.LastQuery.ShouldNotBeNull().NamePrefix.ShouldBe("alpha");
        store.LastQuery.Tags["kind"].ShouldBe("demo");
        store.LastQuery.IncludeActive.ShouldBe(true);
        store.LastQuery.IncludeCompleted.ShouldBe(true);
    }

    [Fact]
    public async Task Query_can_omit_sessions_without_changing_count()
    {
        var store = new TestSessionStore();
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            StartedAt = DateTimeOffset.UtcNow
        });
        await using var node = new SessionQueryNode(
            new SessionQueryOptions { EmitSessionsInResult = false },
            store);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        node.Complete();
        var result = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        result.Value.Count.ShouldBe(1);
        result.Value.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_returns_invalid_request_as_data_and_continues()
    {
        var store = CreateStoreWithQuerySession();
        await using var node = new SessionQueryNode(
            new SessionQueryOptions { EmitSessionsInResult = true },
            store);
        var output = Sink(node.Output);
        var failed = FlowMessage.Create(new SessionQueryRequest { Limit = 0 });
        var succeeded = FlowMessage.Create(new SessionQueryRequest());

        await node.Input.SendAsync(failed);
        await node.Input.SendAsync(succeeded);
        node.Complete();
        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results.Count.ShouldBe(2);
        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.InvalidRequest);
        results[0].CorrelationId.ShouldBe(failed.CorrelationId);
        results[1].Value.Count.ShouldBe(1);
        results[1].CorrelationId.ShouldBe(succeeded.CorrelationId);
    }

    [Fact]
    public async Task Query_returns_store_failure_as_data_and_continues()
    {
        var store = CreateStoreWithQuerySession();
        store.FailNextQuery = true;
        await using var node = new SessionQueryNode(
            new SessionQueryOptions { EmitSessionsInResult = true },
            store);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        node.Complete();
        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.QueryFailed);
        results[1].Value.Count.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Query_rejects_null_store_results_as_data_and_continues()
    {
        var store = CreateStoreWithQuerySession();
        store.ReturnNullQueryResultOnce = true;
        await using var node = new SessionQueryNode(
            new SessionQueryOptions { EmitSessionsInResult = true },
            store);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        node.Complete();
        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoreUnavailable);
        results[0].Error.ShouldNotBeNull().Message.ShouldContain("null result");
        results[1].Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Query_rejects_null_sessions_as_data_and_continues()
    {
        var store = CreateStoreWithQuerySession();
        store.ReturnNullQuerySessionOnce = true;
        await using var node = new SessionQueryNode(
            new SessionQueryOptions { EmitSessionsInResult = true },
            store);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        await node.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest()));
        node.Complete();
        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoreUnavailable);
        results[0].Error.ShouldNotBeNull().Message.ShouldContain("null session");
        results[1].Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Query_rejects_store_results_outside_filter_as_data_and_continues()
    {
        var store = new TestSessionStore { BypassQueryFilteringOnce = true };
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "beta",
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        });
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-2",
            Name = "alpha",
            StartedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z")
        });
        await using var node = new SessionQueryNode(
            new SessionQueryOptions { EmitSessionsInResult = true },
            store);
        var output = Sink(node.Output);
        var request = new SessionQueryRequest { NamePrefix = "alpha" };

        await node.Input.SendAsync(FlowMessage.Create(request));
        await node.Input.SendAsync(FlowMessage.Create(request));
        node.Complete();
        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoredContentInvalid);
        results[0].Error.ShouldNotBeNull().Message.ShouldContain("namePrefix");
        results[1].Value.Sessions.ShouldHaveSingleItem()
            .SessionId.ShouldBe("session-2");
    }

    [Fact]
    public async Task Query_rejects_store_results_over_limit_as_data_and_continues()
    {
        var store = new TestSessionStore { BypassQueryFilteringOnce = true };
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "alpha",
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        });
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-2",
            Name = "alpha",
            StartedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z")
        });
        await using var node = new SessionQueryNode(
            new SessionQueryOptions { EmitSessionsInResult = true },
            store);
        var output = Sink(node.Output);
        var request = new SessionQueryRequest { Name = "alpha", Limit = 1 };

        await node.Input.SendAsync(FlowMessage.Create(request));
        await node.Input.SendAsync(FlowMessage.Create(request));
        node.Complete();
        var results = await DrainUntilCompletedAsync(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        results[0].Error.ShouldNotBeNull().Code.ShouldBe(
            SessionErrorCodeNames.StoredContentInvalid);
        results[0].Error.ShouldNotBeNull().Message.ShouldContain(
            "more sessions than requested");
        results[1].Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Query_emits_started_event()
    {
        await using var node = new SessionQueryNode(
            new SessionQueryOptions(),
            new TestSessionStore());
        var events = Sink(node.Events);

        var started = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        started.Name.ShouldBe(SessionsDiagnosticNames.QueryStarted);
    }

    [Fact]
    public void Query_validates_options_and_dependencies()
    {
        Should.Throw<ArgumentException>(
            () => new SessionQueryNode(
                new SessionQueryOptions { IncludeActive = false, IncludeCompleted = false },
                new TestSessionStore()));
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SessionQueryNode(
                new SessionQueryOptions { Limit = 0 },
                new TestSessionStore()));
        Should.Throw<ArgumentNullException>(
            () => new SessionQueryNode(new SessionQueryOptions(), null!));
    }

    private static SessionContentRecordInput ContentInput(byte value, string? name = null)
        => new()
        {
            Name = name,
            Content = FlowContent.FromBytes(
                new[] { value },
                "application/octet-stream",
                "binary")
        };

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
                items.Add(item);
        }

        return items;
    }

    private static TestSessionStore CreateStoreWithContentRecords(
        int count = 3,
        TimeSpan? step = null)
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
                Payload = JsonSerializer.SerializeToElement(new
                {
                    formatVersion = 1,
                    bytes = Convert.ToBase64String([(byte)(index + 1)]),
                    contentType = "application/octet-stream",
                    encoding = "binary"
                }),
                ContentType = "application/octet-stream"
            });
        }

        return store;
    }

    private static TestSessionStore CreateStoreWithQuerySession()
    {
        var store = new TestSessionStore();
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        });
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
        public int? FailReadAfter { get; set; }
        public bool ReturnNullStartSessionOnce { get; set; }
        public bool ReturnNullAppendOnce { get; set; }
        public bool ReturnNullCompleteOnce { get; set; }
        public bool ReturnNullQueryResultOnce { get; set; }
        public bool ReturnNullQuerySessionOnce { get; set; }
        public bool ReturnNullReadStreamOnce { get; set; }
        public bool ReturnNullReadRecordOnce { get; set; }
        public bool BypassQueryFilteringOnce { get; set; }
        public bool SerializePayloadAsJsonElement { get; set; }
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
            if (ReturnNullStartSessionOnce)
            {
                ReturnNullStartSessionOnce = false;
                return Task.FromResult<SessionMetadata>(null!);
            }

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

            if (ReturnNullAppendOnce)
            {
                ReturnNullAppendOnce = false;
                return Task.FromResult<SessionRecord>(null!);
            }

            var record = new SessionRecord
            {
                SessionId = request.Session.SessionId,
                Sequence = request.Sequence,
                Timestamp = request.Timestamp,
                Type = request.Input.Type,
                Name = request.Input.Name,
                Payload = SerializePayloadAsJsonElement
                    ? JsonSerializer.SerializeToElement(request.Input.Payload)
                    : request.Input.Payload,
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
            if (ReturnNullCompleteOnce)
            {
                ReturnNullCompleteOnce = false;
                return Task.FromResult<SessionMetadata>(null!);
            }

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

            if (ReturnNullQueryResultOnce)
            {
                ReturnNullQueryResultOnce = false;
                return Task.FromResult<IReadOnlyList<SessionMetadata>>(null!);
            }

            if (ReturnNullQuerySessionOnce)
            {
                ReturnNullQuerySessionOnce = false;
                return Task.FromResult<IReadOnlyList<SessionMetadata>>([null!]);
            }

            LastQuery = request;
            IEnumerable<SessionMetadata> query = Sessions.Count > 0
                ? Sessions
                : Metadata is null
                    ? []
                    : [Metadata];

            if (BypassQueryFilteringOnce)
            {
                BypassQueryFilteringOnce = false;
                return Task.FromResult<IReadOnlyList<SessionMetadata>>(
                    query.Select(CopySession).ToArray());
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
                query = query.Where(session => StringComparer.Ordinal.Equals(session.Name, request.Name));
            if (!string.IsNullOrWhiteSpace(request.NamePrefix))
            {
                query = query.Where(session =>
                    session.Name?.StartsWith(request.NamePrefix, StringComparison.Ordinal) == true);
            }
            if (request.StartedFrom.HasValue)
                query = query.Where(session => session.StartedAt >= request.StartedFrom.Value);
            if (request.StartedTo.HasValue)
                query = query.Where(session => session.StartedAt <= request.StartedTo.Value);
            if (request.EndedFrom.HasValue)
                query = query.Where(session => session.EndedAt >= request.EndedFrom.Value);
            if (request.EndedTo.HasValue)
                query = query.Where(session => session.EndedAt <= request.EndedTo.Value);
            if (request.IncludeActive == false)
                query = query.Where(session => session.EndedAt is not null);
            if (request.IncludeCompleted == false)
                query = query.Where(session => session.EndedAt is null);

            foreach (var (key, value) in request.Tags)
            {
                query = query.Where(session =>
                    session.Tags.TryGetValue(key, out var actual) &&
                    StringComparer.Ordinal.Equals(actual, value));
            }

            return Task.FromResult<IReadOnlyList<SessionMetadata>>(query
                .OrderBy(session => session.StartedAt)
                .Take(request.Limit ?? int.MaxValue)
                .Select(CopySession)
                .ToArray());
        }

        public IAsyncEnumerable<SessionRecord> ReadMessagesAsync(
            SessionReadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ReturnNullReadStreamOnce)
            {
                ReturnNullReadStreamOnce = false;
                return null!;
            }

            return ReadMessagesCoreAsync(request, cancellationToken);
        }

        private async IAsyncEnumerable<SessionRecord> ReadMessagesCoreAsync(
            SessionReadRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IEnumerable<SessionRecord> query = Records
                .Where(record => record.SessionId == request.SessionId)
                .OrderBy(record => record.Sequence);
            if (request.StartSequence.HasValue)
                query = query.Where(record => record.Sequence >= request.StartSequence.Value);
            if (request.MaxMessages.HasValue)
                query = query.Take(request.MaxMessages.Value);

            var read = 0;
            foreach (var record in query)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (FailReadAfter.HasValue && read >= FailReadAfter.Value)
                    throw new InvalidOperationException("session read failed mid-stream.");

                await Task.Yield();
                if (ReturnNullReadRecordOnce)
                {
                    ReturnNullReadRecordOnce = false;
                    yield return null!;
                }
                else
                {
                    yield return record;
                }

                read++;
            }
        }

        private void UpsertSession(SessionMetadata session)
        {
            var index = Sessions.FindIndex(existing => existing.SessionId == session.SessionId);
            if (index < 0)
                Sessions.Add(CopySession(session));
            else
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

    private static async Task AdvanceUntilCompletedAsync(
        TrackingFakeTimeProvider timeProvider,
        IFlowNode node,
        TimeSpan step)
    {
        var fired = 0;
        while (!node.Completion.IsCompleted)
        {
            var scheduled = timeProvider.TimerScheduled;
            if (timeProvider.CreatedTimerCount > fired)
            {
                timeProvider.Advance(step);
                fired++;
                continue;
            }

            await Task.WhenAny(scheduled, node.Completion)
                .WaitAsync(TimeSpan.FromSeconds(30));
        }

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
