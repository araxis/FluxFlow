using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Diagnostics;
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
        var store = CreateStoreWithRecords(count: 2);
        var runtimeNode = CreateReplay(new
        {
            sessionId = "session-1",
            mode = "fixedInterval",
            fixedIntervalMilliseconds = 40
        }, store);
        var output = LinkOutput<SessionRecord>(runtimeNode);
        var stopwatch = Stopwatch.StartNew();

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        stopwatch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(25);
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
    public async Task Replay_UsesMultiplierTiming()
    {
        var store = CreateStoreWithRecords(
            count: 2,
            step: TimeSpan.FromMilliseconds(80));
        var runtimeNode = CreateReplay(new
        {
            sessionId = "session-1",
            mode = "multiplier",
            speedMultiplier = 4
        }, store);
        var output = LinkOutput<SessionRecord>(runtimeNode);
        var stopwatch = Stopwatch.StartNew();

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        stopwatch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(10);
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
    }

    private static RuntimeNode CreateRecorder(
        object configuration,
        TestSessionStore store)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSessionsComponents(options => options.UseStore(_ => store));
        registry.TryGetFactory(SessionsComponentTypes.Recorder, out var factory).ShouldBeTrue();
        return factory(CreateContext(SessionsComponentTypes.Recorder, configuration));
    }

    private static RuntimeNode CreateReplay(
        object configuration,
        TestSessionStore store)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSessionsComponents(options => options.UseStore(_ => store));
        registry.TryGetFactory(SessionsComponentTypes.Replay, out var factory).ShouldBeTrue();
        return factory(CreateContext(SessionsComponentTypes.Replay, configuration));
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
        => runtimeNode.FindInput(new PortName(SessionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<SessionRecordInput>>();

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
        public List<SessionRecord> Records { get; } = [];
        public long InitialMessageCount { get; set; }
        public bool FailNextAppend { get; set; }

        public Task<SessionMetadata?> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Metadata?.SessionId == sessionId ? Metadata : null);

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
            return Task.FromResult(Metadata);
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
    }
}
