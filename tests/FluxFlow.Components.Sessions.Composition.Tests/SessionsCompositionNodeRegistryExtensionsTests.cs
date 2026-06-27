using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sessions.Composition;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sessions.Composition.Tests;

public sealed class SessionsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Register_session_nodes_registers_typed_metadata()
    {
        var registry = RegisterAll(new CompositionNodeRegistry());

        var recorder = registry.Registrations[SessionsCompositionNodeTypes.Recorder];
        recorder.Inputs[SessionsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(SessionRecordInput));
        recorder.Outputs[SessionsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(SessionRecord));

        var replay = registry.Registrations[SessionsCompositionNodeTypes.Replay];
        replay.Inputs.ShouldBeEmpty();
        replay.Outputs[SessionsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(SessionRecord));

        var query = registry.Registrations[SessionsCompositionNodeTypes.Query];
        query.Inputs[SessionsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(SessionQueryRequest));
        query.Outputs[SessionsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(SessionQueryResult));
        query.Outputs[SessionsCompositionPortNames.Sessions].MessageType
            .ShouldBe(typeof(SessionMetadata));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_sessions_metadata()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            SessionsCompositionNodeTypes.Recorder,
            SessionsCompositionNodeTypes.Replay,
            SessionsCompositionNodeTypes.Query
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe("Sessions");
            item.SuggestedEditorWidth.ShouldBe(460);
            item.Options.ShouldNotContain(option =>
                option.Name.Value == SessionsCompositionResourceNames.Clock);
            AssertResources(item);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_sessions_ports()
    {
        var metadata = DesignMetadataByType();

        AssertTransformPorts<SessionRecordInput, SessionRecord>(
            metadata[SessionsCompositionNodeTypes.Recorder]);
        AssertSourcePort<SessionRecord>(
            metadata[SessionsCompositionNodeTypes.Replay]);
        AssertQueryPorts(metadata[SessionsCompositionNodeTypes.Query]);
    }

    [Fact]
    public void Design_metadata_provider_describes_sessions_options()
    {
        var metadata = DesignMetadataByType();
        var recorderDefaults = new SessionRecorderOptions();
        var replayDefaults = new SessionReplayOptions();
        var queryDefaults = new SessionQueryOptions();

        AssertOptionNames(
            metadata[SessionsCompositionNodeTypes.Recorder],
            "store",
            "sessionId",
            "name",
            "notes",
            "tags",
            "boundedCapacity");
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Recorder],
            "store",
            OptionValueKind.Text);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Recorder],
            "sessionId",
            OptionValueKind.Text);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Recorder],
            "notes",
            OptionValueKind.MultilineText);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Recorder],
            "tags",
            OptionValueKind.Json);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Recorder],
            "boundedCapacity",
            OptionValueKind.Number,
            recorderDefaults.BoundedCapacity,
            min: 1);

        AssertOptionNames(
            metadata[SessionsCompositionNodeTypes.Replay],
            "store",
            "sessionId",
            "mode",
            "boundedCapacity",
            "startSequence",
            "maxMessages",
            "fixedIntervalMilliseconds",
            "speedMultiplier");
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Replay],
            "sessionId",
            OptionValueKind.Text,
            isRequired: true);
        var mode = AssertOption(
            metadata[SessionsCompositionNodeTypes.Replay],
            "mode",
            OptionValueKind.Enum,
            replayDefaults.Mode.ToString());
        mode.Choices.Select(choice => choice.Value).ShouldBe([
            nameof(SessionReplayMode.RealTime),
            nameof(SessionReplayMode.FixedInterval),
            nameof(SessionReplayMode.Multiplier),
            nameof(SessionReplayMode.Instant)
        ], ignoreOrder: false);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Replay],
            "boundedCapacity",
            OptionValueKind.Number,
            replayDefaults.BoundedCapacity,
            min: 1);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Replay],
            "startSequence",
            OptionValueKind.Number,
            min: 1);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Replay],
            "maxMessages",
            OptionValueKind.Number,
            min: 1);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Replay],
            "fixedIntervalMilliseconds",
            OptionValueKind.Number,
            replayDefaults.FixedIntervalMilliseconds,
            min: 0);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Replay],
            "speedMultiplier",
            OptionValueKind.Number,
            replayDefaults.SpeedMultiplier,
            min: 0.000001);

        AssertOptionNames(
            metadata[SessionsCompositionNodeTypes.Query],
            "store",
            "name",
            "namePrefix",
            "tags",
            "includeActive",
            "includeCompleted",
            "limit",
            "emitSessionsInResult",
            "emitSessionOutputs",
            "boundedCapacity");
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Query],
            "includeActive",
            OptionValueKind.Boolean,
            queryDefaults.IncludeActive);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Query],
            "includeCompleted",
            OptionValueKind.Boolean,
            queryDefaults.IncludeCompleted);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Query],
            "limit",
            OptionValueKind.Number,
            queryDefaults.Limit,
            min: 1);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Query],
            "emitSessionsInResult",
            OptionValueKind.Boolean,
            queryDefaults.EmitSessionsInResult);
        AssertOption(
            metadata[SessionsCompositionNodeTypes.Query],
            "emitSessionOutputs",
            OptionValueKind.Boolean,
            queryDefaults.EmitSessionOutputs);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new SessionsComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(3);
        catalog.TryGet(
            new ComponentType(SessionsCompositionNodeTypes.Recorder),
            out var recorderMetadata).ShouldBeTrue();
        recorderMetadata.ShouldNotBeNull().DisplayName.ShouldBe("Session Recorder");
        catalog.TryGet(
            new ComponentType(SessionsCompositionNodeTypes.Replay),
            out var replayMetadata).ShouldBeTrue();
        replayMetadata.ShouldNotBeNull().DisplayName.ShouldBe("Session Replay");
    }

    [Fact]
    public async Task Hosted_recorder_records_message_preserves_correlation_and_disposes_session()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-21T08:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISessionStore>("sessions", store);
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "recorder",
                    SessionsCompositionNodeTypes.Recorder,
                    node => node
                        .Resource(SessionsCompositionResourceNames.Store, "sessions")
                        .Resource(SessionsCompositionResourceNames.Clock, "fixed")
                        .Configure("store", "diagnostic-only")
                        .Configure("sessionId", "session-1")
                        .Configure("name", "run")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterSessionRecorder())
            .Configure(options => options.StartRuntimeWithHost = false);

        SessionRecorderNode recorderNode;
        var provider = services.BuildServiceProvider();
        try
        {
            await BuildCompositionAsync(provider);

            var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
                .Runtime.ShouldNotBeNull()
                .Nodes.ShouldHaveSingleItem()
                .Descriptor;
            recorderNode = descriptor.Node.ShouldBeOfType<SessionRecorderNode>();
            var input = descriptor.Inputs[SessionsCompositionPortNames.Input]
                .ShouldBeOfType<CompositionInputPort<SessionRecordInput>>();
            var output = descriptor.Outputs[SessionsCompositionPortNames.Output]
                .ShouldBeOfType<CompositionOutputPort<SessionRecord>>();
            var events = Link(descriptor.Events.ShouldNotBeNull());
            var records = Link(output.Source);
            var message = FlowMessage.Create(
                new SessionRecordInput
                {
                    Name = "event",
                    Payload = "payload"
                },
                new CorrelationId("record-1"));

            (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

            var record = await records.ReceiveAsync().WaitAsync(Timeout);
            record.CorrelationId.ShouldBe(message.CorrelationId);
            record.Payload.SessionId.ShouldBe("session-1");
            record.Payload.Sequence.ShouldBe(1);
            record.Payload.Timestamp.ShouldBe(timestamp);
            record.Payload.Name.ShouldBe("event");
            record.Payload.Payload.ShouldBe("payload");

            var started = await events.ReceiveAsync().WaitAsync(Timeout);
            var recorded = await events.ReceiveAsync().WaitAsync(Timeout);
            started.Name.ShouldBe(SessionsDiagnosticNames.RecorderStarted);
            started.Timestamp.ShouldBe(timestamp);
            recorded.Name.ShouldBe(SessionsDiagnosticNames.RecorderRecorded);
            recorded.CorrelationId.ShouldBe(message.CorrelationId);
        }
        finally
        {
            await provider.DisposeAsync();
        }

        await recorderNode.SessionCompleted.WaitAsync(Timeout);
        store.CompletedSession.ShouldNotBeNull().EndedAt.ShouldBe(timestamp);
        store.CompletedSession.MessageCount.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_replay_starts_through_runtime_emits_records_and_uses_clock()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-21T09:00:00Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        var store = new TestSessionStore();
        store.AddSession(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "run",
            StartedAt = startedAt,
            MessageCount = 2
        });
        store.Records.Add(CreateRecord("session-1", 1, startedAt, "first"));
        store.Records.Add(CreateRecord("session-1", 2, startedAt.AddMilliseconds(25), "second"));

        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISessionStore>("sessions", store);
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "replay",
                    SessionsCompositionNodeTypes.Replay,
                    node => node
                        .Resource(SessionsCompositionResourceNames.Store, "sessions")
                        .Resource(SessionsCompositionResourceNames.Clock, "fixed")
                        .Configure("store", "diagnostic-only")
                        .Configure("sessionId", "session-1")
                        .Configure("mode", SessionReplayMode.FixedInterval)
                        .Configure("fixedIntervalMilliseconds", 25)
                        .Configure("maxMessages", 2)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterSessionReplay())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var descriptor = runtime.Nodes.ShouldHaveSingleItem().Descriptor;
        var output = descriptor.Outputs[SessionsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<SessionRecord>>();
        var records = Link(output.Source);
        var events = Link(descriptor.Events.ShouldNotBeNull());

        await runtime.StartAsync();
        var first = await records.ReceiveAsync().WaitAsync(Timeout);
        await clock.TimerScheduled.WaitAsync(Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(25));
        await runtime.Completion.WaitAsync(Timeout);
        var second = await records.ReceiveAsync().WaitAsync(Timeout);
        var eventNames = (await DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();

        first.Payload.Sequence.ShouldBe(1);
        second.Payload.Sequence.ShouldBe(2);
        first.CorrelationId.ShouldNotBe(second.CorrelationId);
        first.CorrelationId.IsEmpty.ShouldBeFalse();
        second.CorrelationId.IsEmpty.ShouldBeFalse();
        eventNames.ShouldContain(SessionsDiagnosticNames.ReplayStarted);
        eventNames.ShouldContain(SessionsDiagnosticNames.ReplayEmitted);
        eventNames.ShouldContain(SessionsDiagnosticNames.ReplayCompleted);
    }

    [Fact]
    public async Task Hosted_recorder_can_resolve_store_factory_resource_and_dispose_owned_lease()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-21T09:30:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        var factory = new RecordingSessionStoreFactory(store);

        await WithNodeAsync(
            SessionsCompositionNodeTypes.Recorder,
            async descriptor =>
            {
                var input = descriptor.Inputs[SessionsCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<SessionRecordInput>>();
                var output = descriptor.Outputs[SessionsCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<SessionRecord>>();
                var records = Link(output.Source);
                var message = FlowMessage.Create(
                    new SessionRecordInput { Name = "event", Payload = "payload" },
                    new CorrelationId("factory-record"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var record = await records.ReceiveAsync().WaitAsync(Timeout);
                record.CorrelationId.ShouldBe(message.CorrelationId);
                record.Payload.SessionId.ShouldBe("session-1");
                record.Payload.Timestamp.ShouldBe(timestamp);
            },
            node => node
                .Resource(SessionsCompositionResourceNames.Store, "factory")
                .Resource(SessionsCompositionResourceNames.Clock, "fixed")
                .Configure("sessionId", "session-1"),
            services =>
            {
                services.AddKeyedSingleton<ISessionStoreFactory>("factory", factory);
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });

        factory.OpenCount.ShouldBe(1);
        factory.Context.ShouldNotBeNull();
        factory.Context.StoreName.ShouldBe("factory");
        factory.Context.SessionId.ShouldBe("session-1");
        factory.Context.Clock.ShouldBe(clock);
        store.CompletedSession.ShouldNotBeNull().SessionId.ShouldBe("session-1");
        store.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_query_emits_result_sessions_branch_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-21T10:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        store.AddSession(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "orders-a",
            StartedAt = timestamp.AddMinutes(-2),
            EndedAt = timestamp.AddMinutes(-1),
            MessageCount = 2,
            Tags = new Dictionary<string, string> { ["kind"] = "order" }
        });
        store.AddSession(new SessionMetadata
        {
            SessionId = "session-2",
            Name = "other",
            StartedAt = timestamp.AddMinutes(-3),
            MessageCount = 1
        });

        await WithNodeAsync(
            SessionsCompositionNodeTypes.Query,
            async descriptor =>
            {
                var input = descriptor.Inputs[SessionsCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<SessionQueryRequest>>();
                var output = descriptor.Outputs[SessionsCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<SessionQueryResult>>();
                var sessions = descriptor.Outputs[SessionsCompositionPortNames.Sessions]
                    .ShouldBeOfType<CompositionOutputPort<SessionMetadata>>();
                var results = Link(output.Source);
                var sessionOutputs = Link(sessions.Source);
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var request = FlowMessage.Create(
                    new SessionQueryRequest
                    {
                        Tags = new Dictionary<string, string> { ["kind"] = "order" }
                    },
                    new CorrelationId("query-1"));

                (await input.Target.SendAsync(request).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                var session = await sessionOutputs.ReceiveAsync().WaitAsync(Timeout);

                result.CorrelationId.ShouldBe(request.CorrelationId);
                result.Payload.Timestamp.ShouldBe(timestamp);
                result.Payload.Count.ShouldBe(1);
                result.Payload.Sessions.ShouldBeEmpty();
                session.CorrelationId.ShouldBe(request.CorrelationId);
                session.Payload.SessionId.ShouldBe("session-1");

                var eventNames = DrainAvailable(events).Select(flowEvent => flowEvent.Name).ToArray();
                eventNames.ShouldContain(SessionsDiagnosticNames.QueryStarted);
                eventNames.ShouldContain(SessionsDiagnosticNames.QueryCompleted);
            },
            node => node
                .Resource(SessionsCompositionResourceNames.Store, "sessions")
                .Resource(SessionsCompositionResourceNames.Clock, "fixed")
                .Configure("store", "diagnostic-only")
                .Configure("namePrefix", "orders")
                .Configure("limit", 10)
                .Configure("emitSessionsInResult", false)
                .Configure("emitSessionOutputs", true),
            services =>
            {
                services.AddKeyedSingleton<ISessionStore>("sessions", store);
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });
    }

    [Fact]
    public async Task Missing_store_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "recorder",
                    SessionsCompositionNodeTypes.Recorder,
                    node => node.Configure("sessionId", "session-1")))
                .Build())
            .RegisterNodes(registry => registry.RegisterSessionRecorder())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                SessionsCompositionResourceNames.Store,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SessionsCompositionNodeTypes.Recorder, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(SessionsCompositionNodeTypes.Replay, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(SessionsCompositionNodeTypes.Replay, "mode", 999, "mode")]
    [InlineData(SessionsCompositionNodeTypes.Replay, "startSequence", 0, "startSequence")]
    [InlineData(SessionsCompositionNodeTypes.Replay, "maxMessages", 0, "maxMessages")]
    [InlineData(SessionsCompositionNodeTypes.Replay, "fixedIntervalMilliseconds", -1, "fixedIntervalMilliseconds")]
    [InlineData(SessionsCompositionNodeTypes.Replay, "speedMultiplier", 0, "speedMultiplier")]
    [InlineData(SessionsCompositionNodeTypes.Query, "limit", 0, "limit")]
    public async Task Invalid_numeric_configuration_surfaces_factory_diagnostic(
        string nodeType,
        string optionName,
        object value,
        string expectedMessage)
    {
        await AssertFactoryDiagnosticAsync(
            nodeType,
            node =>
            {
                node.Resource(SessionsCompositionResourceNames.Store, "sessions")
                    .Configure(optionName, value);
                if (nodeType == SessionsCompositionNodeTypes.Replay)
                {
                    node.Configure("sessionId", "session-1");
                }
            },
            expectedMessage);
    }

    [Fact]
    public async Task Missing_replay_session_id_surfaces_factory_diagnostic()
        => await AssertFactoryDiagnosticAsync(
            SessionsCompositionNodeTypes.Replay,
            node => node.Resource(SessionsCompositionResourceNames.Store, "sessions"),
            "session id");

    [Fact]
    public async Task Factory_failure_disposes_owned_store_lease()
    {
        var store = new TestSessionStore();
        var factory = new RecordingSessionStoreFactory(store);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISessionStoreFactory>("factory", factory);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "replay",
                    SessionsCompositionNodeTypes.Replay,
                    node => node.Resource(SessionsCompositionResourceNames.Store, "factory")))
                .Build())
            .RegisterNodes(registry => registry.RegisterSessionReplay())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("session id", StringComparison.OrdinalIgnoreCase));
        factory.OpenCount.ShouldBe(1);
        store.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Query_excluding_active_and_completed_surfaces_factory_diagnostic()
        => await AssertFactoryDiagnosticAsync(
            SessionsCompositionNodeTypes.Query,
            node => node
                .Resource(SessionsCompositionResourceNames.Store, "sessions")
                .Configure("includeActive", false)
                .Configure("includeCompleted", false),
            "include active");

    [Fact]
    public async Task Recorder_store_failure_emits_error_and_later_messages_continue()
    {
        var store = new TestSessionStore
        {
            FailNextAppend = true
        };

        await WithNodeAsync(
            SessionsCompositionNodeTypes.Recorder,
            async descriptor =>
            {
                var input = descriptor.Inputs[SessionsCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<SessionRecordInput>>();
                var output = descriptor.Outputs[SessionsCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<SessionRecord>>();
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var records = Link(output.Source);
                var bad = FlowMessage.Create(
                    new SessionRecordInput { Name = "bad" },
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    new SessionRecordInput { Name = "good" },
                    new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var record = await records.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(SessionsErrorCodes.RecorderFailed);
                error.CorrelationId.ShouldBe(bad.CorrelationId);
                record.CorrelationId.ShouldBe(good.CorrelationId);
                record.Payload.Name.ShouldBe("good");
            },
            node => node
                .Resource(SessionsCompositionResourceNames.Store, "sessions")
                .Configure("sessionId", "session-1"),
            services => services.AddKeyedSingleton<ISessionStore>("sessions", store));
    }

    [Fact]
    public async Task Query_store_failure_emits_error_and_later_messages_continue()
    {
        var store = new TestSessionStore
        {
            FailNextQuery = true
        };
        store.AddSession(new SessionMetadata
        {
            SessionId = "session-1",
            Name = "run",
            StartedAt = DateTimeOffset.Parse("2026-06-21T10:30:00Z"),
            MessageCount = 1
        });

        await WithNodeAsync(
            SessionsCompositionNodeTypes.Query,
            async descriptor =>
            {
                var input = descriptor.Inputs[SessionsCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<SessionQueryRequest>>();
                var output = descriptor.Outputs[SessionsCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<SessionQueryResult>>();
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var results = Link(output.Source);
                var bad = FlowMessage.Create(
                    new SessionQueryRequest(),
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    new SessionQueryRequest(),
                    new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var result = await results.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(SessionsErrorCodes.QueryFailed);
                error.CorrelationId.ShouldBe(bad.CorrelationId);
                result.CorrelationId.ShouldBe(good.CorrelationId);
                result.Payload.Count.ShouldBe(1);
            },
            node => node.Resource(SessionsCompositionResourceNames.Store, "sessions"),
            services => services.AddKeyedSingleton<ISessionStore>("sessions", store));
    }

    private static async Task WithNodeAsync(
        string nodeType,
        Func<ComposedNode, Task> run,
        Action<NodeDefinitionBuilder> configureNode,
        Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "session",
                    nodeType,
                    configureNode))
                .Build())
            .RegisterNodes(registry => RegisterAll(registry))
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;

        await run(descriptor);
    }

    private static async Task AssertFactoryDiagnosticAsync(
        string nodeType,
        Action<NodeDefinitionBuilder> configureNode,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISessionStore>("sessions", new TestSessionStore());
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "session",
                    nodeType,
                    configureNode))
                .Build())
            .RegisterNodes(registry => RegisterAll(registry))
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    private static CompositionNodeRegistry RegisterAll(CompositionNodeRegistry registry)
        => registry
            .RegisterSessionRecorder()
            .RegisterSessionReplay()
            .RegisterSessionQuery();

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => new SessionsComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static void AssertTransformPorts<TInput, TOutput>(
        ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(SessionsCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType.ShouldBe(typeof(TInput).Name);
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        AssertOutputPort(
            output,
            SessionsCompositionPortNames.Output,
            typeof(TOutput).Name,
            order: 1,
            isPrimary: true);
    }

    private static void AssertSourcePort<TOutput>(
        ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(1);

        AssertOutputPort(
            metadata.Ports[0],
            SessionsCompositionPortNames.Output,
            typeof(TOutput).Name,
            order: 0,
            isPrimary: true);
    }

    private static void AssertQueryPorts(ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(3);

        metadata.Ports[0].Name.Value.ShouldBe(SessionsCompositionPortNames.Input);
        metadata.Ports[0].Direction.ShouldBe(PortDirection.Input);
        metadata.Ports[0].Order.ShouldBe(0);
        metadata.Ports[0].ValueType.ShouldBe(nameof(SessionQueryRequest));
        metadata.Ports[0].IsPrimary.ShouldBeTrue();

        AssertOutputPort(
            metadata.Ports[1],
            SessionsCompositionPortNames.Output,
            nameof(SessionQueryResult),
            order: 1,
            isPrimary: true);
        AssertOutputPort(
            metadata.Ports[2],
            SessionsCompositionPortNames.Sessions,
            nameof(SessionMetadata),
            order: 2);
    }

    private static void AssertOutputPort(
        PortDesignMetadata port,
        string name,
        string valueType,
        int order,
        bool isPrimary = false)
    {
        port.Name.Value.ShouldBe(name);
        port.Direction.ShouldBe(PortDirection.Output);
        port.Order.ShouldBe(order);
        port.ValueType.ShouldBe(valueType);
        port.IsPrimary.ShouldBe(isPrimary);
    }

    private static void AssertOptionNames(
        ComponentDesignMetadata metadata,
        params string[] names)
        => metadata.Options.Select(option => option.Name.Value)
            .ShouldBe(names, ignoreOrder: false);

    private static OptionDesignMetadata AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue = null,
        double? min = null,
        bool isRequired = false)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
        option.IsRequired.ShouldBe(isRequired);
        return option;
    }

    private static void AssertResources(ComponentDesignMetadata metadata)
    {
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType)).ShouldBe([
            (SessionsCompositionResourceNames.Store, 0, true, $"{nameof(ISessionStore)} or {nameof(ISessionStoreFactory)}"),
            (SessionsCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
        ]);
    }

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static List<T> DrainAvailable<T>(BufferBlock<T> sink)
    {
        var items = new List<T>();
        while (sink.TryReceive(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> sink)
    {
        var items = new List<T>();
        while (await sink.OutputAvailableAsync().WaitAsync(Timeout))
        {
            while (sink.TryReceive(out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static SessionRecord CreateRecord(
        string sessionId,
        long sequence,
        DateTimeOffset timestamp,
        string name)
        => new()
        {
            SessionId = sessionId,
            Sequence = sequence,
            Timestamp = timestamp,
            Name = name,
            Payload = name
        };

    private sealed class TrackingFakeTimeProvider : FakeTimeProvider
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _timerScheduled = CreateSource();
        private bool _timerWasScheduled;

        public TrackingFakeTimeProvider(DateTimeOffset startDateTime)
            : base(startDateTime)
        {
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            lock (_gate)
            {
                _timerWasScheduled = true;
                _timerScheduled.TrySetResult();
            }

            return timer;
        }

        public Task TimerScheduled
        {
            get
            {
                lock (_gate)
                {
                    return _timerWasScheduled
                        ? Task.CompletedTask
                        : _timerScheduled.Task;
                }
            }
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestSessionStore : ISessionStore, IAsyncDisposable
    {
        public List<SessionMetadata> Sessions { get; } = [];
        public List<SessionRecord> Records { get; } = [];
        public bool FailNextAppend { get; set; }
        public bool FailNextQuery { get; set; }
        public int DisposeCount { get; private set; }
        public SessionMetadata? CompletedSession { get; private set; }

        public void AddSession(SessionMetadata session) => UpsertSession(session);

        public Task<SessionMetadata?> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Sessions
                .FirstOrDefault(session => StringComparer.Ordinal.Equals(
                    session.SessionId,
                    sessionId)));
        }

        public Task<SessionMetadata> StartSessionAsync(
            SessionStartRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = new SessionMetadata
            {
                SessionId = string.IsNullOrWhiteSpace(request.SessionId)
                    ? "generated-session"
                    : request.SessionId.Trim(),
                Name = request.Name,
                StartedAt = request.StartedAt,
                MessageCount = 0,
                Notes = request.Notes,
                Tags = CopyDictionary(request.Tags)
            };
            UpsertSession(session);
            return Task.FromResult(session);
        }

        public Task<SessionRecord> AppendMessageAsync(
            SessionAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                Attributes = CopyDictionary(request.Input.Attributes)
            };
            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<SessionMetadata> CompleteSessionAsync(
            SessionCompleteRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedSession = request.Session with
            {
                EndedAt = request.EndedAt,
                MessageCount = request.MessageCount,
                Tags = CopyDictionary(request.Session.Tags)
            };
            UpsertSession(CompletedSession);
            return Task.FromResult(CompletedSession);
        }

        public Task<IReadOnlyList<SessionMetadata>> QuerySessionsAsync(
            SessionQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextQuery)
            {
                FailNextQuery = false;
                throw new InvalidOperationException("query failed");
            }

            IEnumerable<SessionMetadata> query = Sessions;
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                query = query.Where(session =>
                    StringComparer.Ordinal.Equals(session.Name, request.Name));
            }

            if (!string.IsNullOrWhiteSpace(request.NamePrefix))
            {
                query = query.Where(session =>
                    session.Name?.StartsWith(
                        request.NamePrefix,
                        StringComparison.Ordinal) == true);
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
                .Where(record => StringComparer.Ordinal.Equals(
                    record.SessionId,
                    request.SessionId))
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
                yield return CopyRecord(record);
            }
        }

        private void UpsertSession(SessionMetadata session)
        {
            var index = Sessions.FindIndex(existing =>
                StringComparer.Ordinal.Equals(existing.SessionId, session.SessionId));
            if (index < 0)
            {
                Sessions.Add(CopySession(session));
                return;
            }

            Sessions[index] = CopySession(session);
        }

        private static SessionRecord CopyRecord(SessionRecord record)
            => record with
            {
                Attributes = CopyDictionary(record.Attributes)
            };

        private static SessionMetadata CopySession(SessionMetadata session)
            => session with
            {
                Tags = CopyDictionary(session.Tags)
            };

        private static Dictionary<string, string> CopyDictionary(
            Dictionary<string, string>? source)
            => source is null
                ? []
                : new Dictionary<string, string>(source, StringComparer.Ordinal);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSessionStoreFactory(ISessionStore store) : ISessionStoreFactory
    {
        public int OpenCount { get; private set; }
        public SessionStoreContext? Context { get; private set; }

        public ValueTask<SessionStoreLease> OpenAsync(
            SessionStoreContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            Context = context;
            return ValueTask.FromResult(SessionStoreLease.Owned(store));
        }
    }
}
