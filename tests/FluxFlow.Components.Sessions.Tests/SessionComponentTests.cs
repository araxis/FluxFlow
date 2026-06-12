using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Components.Sessions.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

public sealed class SessionComponentTests
{
    [Fact]
    public async Task Recorder_WritesMessagesInOrder()
    {
        var store = new TestSessionStore();
        var runtimeNode = CreateRecorder(new
        {
            sessionId = "session-1",
            name = "sample",
            boundedCapacity = 4
        }, store);
        var input = GetInput(runtimeNode);
        var output = LinkOutput<SessionRecord>(runtimeNode);
        var firstTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new SessionRecordInput
        {
            Timestamp = firstTimestamp,
            Name = "first",
            Payload = "a"
        });
        await input.Target.SendAsync(new SessionRecordInput
        {
            Timestamp = firstTimestamp.AddSeconds(1),
            Name = "second",
            Payload = "b"
        });
        input.Target.Complete();

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Sequence.ShouldBe(1);
        second.Sequence.ShouldBe(2);
        store.Records.Select(record => record.Name).ShouldBe(["first", "second"]);
        store.Metadata.ShouldNotBeNull();
        store.Metadata.SessionId.ShouldBe("session-1");
        store.Metadata.MessageCount.ShouldBe(2);
        store.Metadata.EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Recorder_ReportsAppendFailureAndContinues()
    {
        var store = new TestSessionStore { FailNextAppend = true };
        var runtimeNode = CreateRecorder(new
        {
            sessionId = "session-1"
        }, store);
        var input = GetInput(runtimeNode);
        var output = LinkOutput<SessionRecord>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, SessionsComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new SessionRecordInput { Name = "bad" });
        await input.Target.SendAsync(new SessionRecordInput { Name = "good" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var recorded = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(SessionsErrorCodes.RecorderFailed);
        recorded.Sequence.ShouldBe(1);
        recorded.Name.ShouldBe("good");
        store.Records.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Recorder_ContinuesFromExistingMessageCountAndCopiesNullAttributes()
    {
        var store = new TestSessionStore { InitialMessageCount = 5 };
        var runtimeNode = CreateRecorder(new
        {
            sessionId = "session-1"
        }, store);
        var input = GetInput(runtimeNode);
        var output = LinkOutput<SessionRecord>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new SessionRecordInput
        {
            Name = "next",
            Attributes = null!
        });
        input.Target.Complete();

        var recorded = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        recorded.Sequence.ShouldBe(6);
        recorded.Attributes.ShouldBeEmpty();
        store.Metadata.ShouldNotBeNull();
        store.Metadata.MessageCount.ShouldBe(6);
    }

    [Fact]
    public async Task Recorder_UsesConfiguredClockForDefaultTimestamps()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new RecordingSessionClock { UtcNow = timestamp };
        var store = new TestSessionStore();
        var runtimeNode = CreateRecorder(
            new
            {
                sessionId = "session-1"
            },
            store,
            options => options.UseClock(clock));
        var input = GetInput(runtimeNode);
        var output = LinkOutput<SessionRecord>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new SessionRecordInput { Name = "timed" });
        input.Target.Complete();

        var record = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        record.Timestamp.ShouldBe(timestamp);
        store.Metadata.ShouldNotBeNull();
        store.Metadata.StartedAt.ShouldBe(timestamp);
        store.Metadata.EndedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Replay_EmitsMessagesInOrder()
    {
        var store = CreateStoreWithRecords();
        var runtimeNode = CreateReplay(new
        {
            sessionId = "session-1",
            mode = "instant"
        }, store);
        var output = LinkOutput<SessionRecord>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var records = await DrainUntilCompletedAsync(output);
        records.Select(record => record.Sequence).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Replay_SupportsFixedInterval()
    {
        var clock = new RecordingSessionClock();
        var store = CreateStoreWithRecords(count: 3);
        var runtimeNode = CreateReplay(
            new
            {
                sessionId = "session-1",
                mode = "fixedInterval",
                fixedIntervalMilliseconds = 40
            },
            store,
            options => options.UseClock(clock));
        var output = LinkOutput<SessionRecord>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).Count.ShouldBe(3);
        clock.Delays.ShouldBe(
            [
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(40)
            ]);
    }

    [Fact]
    public async Task Replay_SupportsCancellation()
    {
        var store = CreateStoreWithRecords(count: 3);
        var runtimeNode = CreateReplay(new
        {
            sessionId = "session-1",
            mode = "fixedInterval",
            fixedIntervalMilliseconds = 500
        }, store);
        var output = LinkOutput<SessionRecord>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Sequence.ShouldBe(1);
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_StopsWhenOutputDeclinesDelivery()
    {
        var clock = new RecordingSessionClock();
        var store = CreateStoreWithRecords(count: 3);
        var runtimeNode = CreateReplay(
            new
            {
                sessionId = "session-1",
                mode = "fixedInterval",
                fixedIntervalMilliseconds = 10
            },
            store,
            options => options.UseClock(clock));

        // Completing the node before the replay starts completes the output,
        // so every subsequent send is declined.
        runtimeNode.Node.Complete();
        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await ((IAsyncDisposable)runtimeNode.Node).DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        // The replay loop must stop on the first declined send instead of
        // iterating (and delaying) through the remaining records.
        clock.Delays.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replay_UsesMultiplierTiming()
    {
        var clock = new RecordingSessionClock();
        var store = CreateStoreWithRecords(
            count: 2,
            step: TimeSpan.FromMilliseconds(80));
        var runtimeNode = CreateReplay(
            new
            {
                sessionId = "session-1",
                mode = "multiplier",
                speedMultiplier = 4
            },
            store,
            options => options.UseClock(clock));
        var output = LinkOutput<SessionRecord>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).Count.ShouldBe(2);
        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(20)]);
    }

    [Fact]
    public async Task Query_EmitsResultAndSessionOutputs()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 13, 0, 0, TimeSpan.Zero);
        var clock = new RecordingSessionClock { UtcNow = timestamp };
        var store = new TestSessionStore();
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "alpha-one",
            StartedAt = timestamp.AddMinutes(-5),
            Tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "demo"
            }
        });
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-2",
            Name = "beta-one",
            StartedAt = timestamp.AddMinutes(-3),
            EndedAt = timestamp.AddMinutes(-1),
            Tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "demo"
            }
        });
        var runtimeNode = CreateQuery(
            new
            {
                namePrefix = "alpha",
                tags = new Dictionary<string, string>
                {
                    ["kind"] = "demo"
                }
            },
            store,
            options => options.UseClock(clock));
        var input = GetInput<SessionQueryRequest>(runtimeNode);
        var output = LinkOutput<SessionQueryResult>(runtimeNode);
        var sessions = LinkOutput<SessionMetadata>(runtimeNode, SessionsComponentPorts.Sessions);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new SessionQueryRequest
        {
            CorrelationId = "corr-1"
        });
        input.Target.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var session = await sessions.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Timestamp.ShouldBe(timestamp);
        result.Count.ShouldBe(1);
        result.CorrelationId.ShouldBe("corr-1");
        result.Sessions.ShouldHaveSingleItem().SessionId.ShouldBe("session-1");
        session.SessionId.ShouldBe("session-1");
        store.LastQuery.ShouldNotBeNull();
        store.LastQuery.NamePrefix.ShouldBe("alpha");
        store.LastQuery.Tags["kind"].ShouldBe("demo");
        store.LastQuery.IncludeActive.ShouldBe(true);
        store.LastQuery.IncludeCompleted.ShouldBe(true);
    }

    [Fact]
    public async Task Query_ReportsInvalidRequestAndContinues()
    {
        var store = new TestSessionStore();
        store.Sessions.Add(new SessionMetadata
        {
            SessionId = "session-1",
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        });
        var runtimeNode = CreateQuery(new { }, store);
        var input = GetInput<SessionQueryRequest>(runtimeNode);
        var output = LinkOutput<SessionQueryResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, SessionsComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new SessionQueryRequest { Limit = 0 });
        await input.Target.SendAsync(new SessionQueryRequest());
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(SessionsErrorCodes.InvalidQuery);
        result.Count.ShouldBe(1);
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
        var runtimeNode = CreateQuery(new { }, store);
        var input = GetInput<SessionQueryRequest>(runtimeNode);
        var output = LinkOutput<SessionQueryResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, SessionsComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new SessionQueryRequest());
        await input.Target.SendAsync(new SessionQueryRequest());
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(SessionsErrorCodes.QueryFailed);
        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Replay_EmitsDiagnostics()
    {
        var store = CreateStoreWithRecords(count: 1);
        var runtimeNode = CreateReplay(new
        {
            sessionId = "session-1",
            mode = "instant"
        }, store);
        var output = LinkOutput<SessionRecord>(runtimeNode);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var names = (await DrainDiagnosticsUntilCompletedAsync(diagnostics))
            .Select(diagnostic => diagnostic.Name)
            .ToArray();
        names.ShouldContain(SessionsDiagnosticNames.ReplayStarted);
        names.ShouldContain(SessionsDiagnosticNames.ReplayEmitted);
        names.ShouldContain(SessionsDiagnosticNames.ReplayCompleted);
    }

    [Fact]
    public async Task Replay_FailsStartupWhenSessionIsMissing()
    {
        var store = new TestSessionStore();
        var runtimeNode = CreateReplay(new
        {
            sessionId = "missing"
        }, store);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());

        exception.Message.ShouldContain("missing");
    }

    [Fact]
    public void Replay_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateReplay(new { }, new TestSessionStore()));

        exception.Message.ShouldContain("sessionId");
    }

    [Fact]
    public void Recorder_RequiresStore()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSessionsComponents();
        registry.TryGetFactory(SessionsComponentTypes.Recorder, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(CreateContext(SessionsComponentTypes.Recorder, new { })));

        exception.Message.ShouldContain("session store");
    }

    [Fact]
    public void RegisterSessionsComponents_RegistersNodes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSessionsComponents(options => options.UseStore(_ => new TestSessionStore()));

        registry.TryGetFactory(SessionsComponentTypes.Recorder, out _).ShouldBeTrue();
        registry.TryGetFactory(SessionsComponentTypes.Replay, out _).ShouldBeTrue();
        registry.TryGetFactory(SessionsComponentTypes.Query, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreateRecorder(
        object configuration,
        TestSessionStore store,
        Action<SessionsComponentOptions>? configure = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSessionsComponents(options =>
            {
                options.UseStore(_ => store);
                configure?.Invoke(options);
            });
        registry.TryGetFactory(SessionsComponentTypes.Recorder, out var factory).ShouldBeTrue();
        return factory(CreateContext(SessionsComponentTypes.Recorder, configuration));
    }

    private static RuntimeNode CreateReplay(
        object configuration,
        TestSessionStore store,
        Action<SessionsComponentOptions>? configure = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSessionsComponents(options =>
            {
                options.UseStore(_ => store);
                configure?.Invoke(options);
            });
        registry.TryGetFactory(SessionsComponentTypes.Replay, out var factory).ShouldBeTrue();
        return factory(CreateContext(SessionsComponentTypes.Replay, configuration));
    }

    private static RuntimeNode CreateQuery(
        object configuration,
        TestSessionStore store,
        Action<SessionsComponentOptions>? configure = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSessionsComponents(options =>
            {
                options.UseStore(_ => store);
                configure?.Invoke(options);
            });
        registry.TryGetFactory(SessionsComponentTypes.Query, out var factory).ShouldBeTrue();
        return factory(CreateContext(SessionsComponentTypes.Query, configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(
        NodeType nodeType,
        object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("session"),
            new NodeDefinition
            {
                Type = nodeType,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<SessionRecordInput> GetInput(RuntimeNode runtimeNode)
        => GetInput<SessionRecordInput>(runtimeNode);

    private static InputPort<T> GetInput<T>(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(SessionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<T>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName = SessionsComponentPorts.Output)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }

    private static async Task<IReadOnlyList<SessionRecord>> DrainUntilCompletedAsync(
        BufferBlock<SessionRecord> output)
    {
        var records = new List<SessionRecord>();
        while (await output.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            while (output.TryReceive(out var record))
            {
                records.Add(record);
            }
        }

        return records;
    }

    private static async Task<IReadOnlyList<FlowDiagnostic>> DrainDiagnosticsUntilCompletedAsync(
        BufferBlock<FlowDiagnostic> output)
    {
        var diagnostics = new List<FlowDiagnostic>();
        while (await output.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            while (output.TryReceive(out var diagnostic))
            {
                diagnostics.Add(diagnostic);
            }
        }

        return diagnostics;
    }

    private static TestSessionStore CreateStoreWithRecords(
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

    private sealed class RecordingSessionClock : ISessionClock
    {
        public DateTimeOffset UtcNow { get; init; } =
            new(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);

        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return ValueTask.CompletedTask;
        }
    }
}
