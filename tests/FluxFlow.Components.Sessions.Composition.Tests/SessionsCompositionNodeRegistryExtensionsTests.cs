using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sessions.Composition;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Diagnostics;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Sessions.Composition.Tests;

public sealed class SessionsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const string WorkflowName = "main";
    private const string ComponentName = "session";
    private const string RecorderType = "test.message-recorder";
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort(
        WorkflowName,
        ComponentName,
        SessionsCompositionPortNames.Input);
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort(
        WorkflowName,
        ComponentName,
        SessionsCompositionPortNames.Output);
    private static readonly ApplicationAddress Events = ApplicationAddress.WorkflowPort(
        WorkflowName,
        ComponentName,
        CompositionComponentEvents.PortName);

    [Fact]
    public void Register_session_nodes_registers_canonical_metadata()
    {
        var registry = RegisterAll(new CompositionNodeRegistry());

        var recorder = registry.Registrations[SessionsCompositionNodeTypes.Recorder];
        recorder.Inputs[SessionsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(SessionContentRecordInput));
        recorder.Outputs[SessionsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<SessionContentRecord>));

        var replay = registry.Registrations[SessionsCompositionNodeTypes.Replay];
        replay.Inputs.ShouldBeEmpty();
        replay.Outputs[SessionsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<SessionContentRecord>));

        var query = registry.Registrations[SessionsCompositionNodeTypes.Query];
        query.Inputs[SessionsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(SessionQueryRequest));
        query.Outputs[SessionsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<SessionQueryOutcome>));
        query.Outputs.Keys.ShouldBe([
            SessionsCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
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
            item.Category.ShouldBe(new ComponentCategory("Sessions"));
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

        AssertTransformPorts(
            nameof(SessionContentRecordInput),
            "FlowResult<SessionContentRecord>",
            metadata[SessionsCompositionNodeTypes.Recorder]);
        AssertSourcePort(
            "FlowResult<SessionContentRecord>",
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
            "sessionId",
            "sessionName",
            "notes",
            "tags",
            "boundedCapacity");
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
        mode.Choices.Select(choice => choice.Value.Value).ShouldBe([
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
            "sessionName",
            "namePrefix",
            "tags",
            "includeActive",
            "includeCompleted",
            "limit",
            "emitSessionsInResult",
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
    }

    [Fact]
    public void Design_metadata_provider_describes_sessions_option_hints()
    {
        var metadata = DesignMetadataByType();

        var recorder = OptionsByName(metadata[SessionsCompositionNodeTypes.Recorder]);
        AssertOptionHints(recorder["sessionId"], "Session", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(recorder["sessionName"], "Session", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(recorder["notes"], "Session", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(recorder["tags"], "Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(recorder["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var replay = OptionsByName(metadata[SessionsCompositionNodeTypes.Replay]);
        AssertOptionHints(replay["sessionId"], "Session", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(replay["mode"], "Replay", OptionDesignMetadataAttributeValues.Primary);
        AssertOptionHints(replay["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(replay["startSequence"], "Replay", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(replay["maxMessages"], "Replay", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(replay["fixedIntervalMilliseconds"], "Timing", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(replay["speedMultiplier"], "Timing", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var query = OptionsByName(metadata[SessionsCompositionNodeTypes.Query]);
        AssertOptionHints(query["sessionName"], "Filtering", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(query["namePrefix"], "Filtering", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(query["tags"], "Filtering", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(query["includeActive"], "Filtering", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(query["includeCompleted"], "Filtering", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(query["limit"], "Results", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(query["emitSessionsInResult"], "Results", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(query["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_sessions_resource_picker_hints()
    {
        var metadata = DesignMetadataByType();

        foreach (var item in metadata.Values)
        {
            var resources = ResourcesByName(item);

            AssertResourceHints(
                resources[SessionsCompositionResourceNames.Store],
                ResourceDesignMetadataAttributeValues.Store,
                "Resources.{name}");
            AssertResourceHints(
                resources[SessionsCompositionResourceNames.Clock],
                ResourceDesignMetadataAttributeValues.Clock,
                "Resources.{name}");
        }
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
        recorderMetadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("Session Recorder");
        catalog.TryGet(
            new ComponentType(SessionsCompositionNodeTypes.Replay),
            out var replayMetadata).ShouldBeTrue();
        replayMetadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("Session Replay");
    }

    [Fact]
    public async Task Canonical_host_records_content_preserves_lineage_and_closes_session()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-21T08:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();

        await using (var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Recorder,
            Properties(
                ("sessionId", "session-1"),
                ("sessionName", "run"),
                ("boundedCapacity", 8)),
            store,
            clock: clock))
        {
            host.StartResult.Succeeded.ShouldBeTrue();
            var ports = host.GetRequiredPorts();
            var resultReceive = ports.ReceiveAsync<FlowResult<SessionContentRecord>>(Output, Timeout);
            var eventObservation = (await ports.ObserveAsync<CompositionComponentEvent>(Events))
                .Observation.ShouldNotBeNull();
            await using var observation = eventObservation;
            var message = FlowMessage.Create(
                new SessionContentRecordInput
                {
                    Name = "event",
                    Content = FlowContent.FromBytes(
                        new byte[] { 1, 2, 3 },
                        "application/octet-stream")
                },
                new CorrelationId("record-1"));

            (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
            var result = (await resultReceive).Message.ShouldNotBeNull();

            result.CorrelationId.ShouldBe(message.CorrelationId);
            var stored = result.Payload.Value.ShouldNotBeNull();
            stored.SessionId.ShouldBe("session-1");
            stored.Sequence.ShouldBe(1);
            stored.Timestamp.ShouldBe(timestamp);
            stored.Content.OriginalBytes.ToArray().ShouldBe(new byte[] { 1, 2, 3 });

            var firstEvent = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);
            var secondEvent = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);
            new[] { firstEvent.Payload.Name, secondEvent.Payload.Name }.ShouldBe([
                SessionsDiagnosticNames.RecorderStarted,
                SessionsDiagnosticNames.RecorderRecorded
            ]);
            secondEvent.CorrelationId.ShouldBe(message.CorrelationId);
        }

        store.CompletedSession.ShouldNotBeNull().EndedAt.ShouldBe(timestamp);
        store.CompletedSession.MessageCount.ShouldBe(1);
    }

    [Fact]
    public async Task Canonical_host_starts_replay_source_and_emits_records()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-21T09:00:00Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        var store = new TestSessionStore();
        await SeedContentRecordsAsync(
            store,
            clock,
            "session-1",
            (startedAt, "first", new byte[] { 1 }),
            (startedAt.AddMilliseconds(25), "second", new byte[] { 2 }));
        var tracker = new MessageTracker<FlowResult<SessionContentRecord>>(2);
        var properties = Properties(
            ("sessionId", "session-1"),
            ("mode", SessionReplayMode.FixedInterval),
            ("fixedIntervalMilliseconds", 25),
            ("maxMessages", 2),
            ("boundedCapacity", 8),
            (SessionsCompositionPortNames.Output, "recorder.Input"));

        await using var host = await StartSourceHostAsync(properties, store, clock, tracker);
        host.StartResult.Succeeded.ShouldBeTrue();
        await clock.TimerScheduled.WaitAsync(Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var records = await tracker.Completion.WaitAsync(Timeout);

        records.Select(record => record.Payload.Value.ShouldNotBeNull().Sequence)
            .ShouldBe([1L, 2L]);
        records[0].CorrelationId.ShouldNotBe(records[1].CorrelationId);
        records.ShouldAllBe(record => !record.CorrelationId.IsEmpty);
    }

    [Fact]
    public async Task Canonical_host_resolves_store_factory_and_disposes_owned_lease()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-21T09:30:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new TestSessionStore();
        var factory = new RecordingSessionStoreFactory(store);

        await using (var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Recorder,
            Properties(("sessionId", "session-1")),
            factory: factory,
            clock: clock))
        {
            host.StartResult.Succeeded.ShouldBeTrue();
            var ports = host.GetRequiredPorts();
            var receive = ports.ReceiveAsync<FlowResult<SessionContentRecord>>(Output, Timeout);
            var message = FlowMessage.Create(new SessionContentRecordInput
            {
                Content = FlowContent.FromBytes(new byte[] { 7, 8 })
            });

            (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
            (await receive).Message.ShouldNotBeNull()
                .Payload.Value.ShouldNotBeNull().Timestamp.ShouldBe(timestamp);
        }

        factory.OpenCount.ShouldBe(1);
        factory.Context.ShouldNotBeNull().StoreName.ShouldBe("Resources.sessions");
        factory.Context.SessionId.ShouldBe("session-1");
        factory.Context.Clock.ShouldBe(clock);
        store.CompletedSession.ShouldNotBeNull().SessionId.ShouldBe("session-1");
        store.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Canonical_host_queries_with_options_clock_and_one_output()
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
            Tags = new Dictionary<string, string> { ["kind"] = "order" }
        });
        store.AddSession(new SessionMetadata
        {
            SessionId = "session-2",
            Name = "other",
            StartedAt = timestamp.AddMinutes(-3)
        });
        await using var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Query,
            Properties(
                ("namePrefix", "orders"),
                ("limit", 10),
                ("emitSessionsInResult", false)),
            store,
            clock: clock);
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var receive = ports.ReceiveAsync<FlowResult<SessionQueryOutcome>>(Output, Timeout);
        var request = FlowMessage.Create(
            new SessionQueryRequest
            {
                Tags = new Dictionary<string, string> { ["kind"] = "order" }
            },
            new CorrelationId("query-1"));

        (await ports.SendAsync(Input, request)).IsAccepted.ShouldBeTrue();
        var result = (await receive).Message.ShouldNotBeNull();

        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Payload.Timestamp.ShouldBe(timestamp);
        store.LastQuery.ShouldNotBeNull().NamePrefix.ShouldBe("orders");
        store.LastQuery.Name.ShouldBeNull();
        store.LastQuery.Tags["kind"].ShouldBe("order");
        store.LastQuery.Tags.Count.ShouldBe(1);
        store.LastQuery.IncludeActive.ShouldBe(true);
        store.LastQuery.IncludeCompleted.ShouldBe(true);
        store.LastQuery.Limit.ShouldBe(10);
        store.Sessions.Count.ShouldBe(2);
        store.Sessions[0].Name.ShouldBe("orders-a");
        store.Sessions[0].Tags["kind"].ShouldBe("order");
        result.Payload.Value.ShouldNotBeNull().Count.ShouldBe(1);
        result.Payload.Value.ShouldNotBeNull().Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_store_reference_rejects_canonical_revision()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                SessionsCompositionNodeTypes.Recorder,
                Properties(("sessionId", "session-1")),
                componentName: ComponentName),
            registry => registry.RegisterSessionRecorder());

        AssertPreparationFailure(host, SessionsCompositionResourceNames.Store);
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
    public async Task Invalid_configuration_rejects_canonical_revision(
        string componentType,
        string optionName,
        object value,
        string expectedMessage)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [optionName] = value
        };
        if (componentType == SessionsCompositionNodeTypes.Replay)
            properties["sessionId"] = "session-1";

        await using var host = await StartHostAsync(
            componentType,
            properties,
            new TestSessionStore());

        AssertPreparationFailure(host, expectedMessage);
    }

    [Fact]
    public async Task Missing_replay_session_id_rejects_canonical_revision()
    {
        await using var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Replay,
            Properties(),
            new TestSessionStore());

        AssertPreparationFailure(host, "session id");
    }

    [Fact]
    public async Task Factory_failure_disposes_owned_store_lease()
    {
        var store = new TestSessionStore();
        var factory = new RecordingSessionStoreFactory(store);
        await using var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Replay,
            Properties(),
            factory: factory);

        AssertPreparationFailure(host, "session id");
        factory.OpenCount.ShouldBe(1);
        store.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Query_excluding_active_and_completed_rejects_canonical_revision()
    {
        await using var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Query,
            Properties(("includeActive", false), ("includeCompleted", false)),
            new TestSessionStore());

        AssertPreparationFailure(host, "include active");
    }

    [Fact]
    public async Task Recorder_store_failure_is_output_data_and_later_messages_continue()
    {
        var store = new TestSessionStore { FailNextAppend = true };
        await using var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Recorder,
            Properties(("sessionId", "session-1")),
            store);
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var observed = (await ports.ObserveAsync<FlowResult<SessionContentRecord>>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = observed;
        var failed = FlowMessage.Create(new SessionContentRecordInput
        {
            Name = "failed",
            Content = FlowContent.FromBytes(new byte[] { 1 })
        });
        var succeeded = FlowMessage.Create(new SessionContentRecordInput
        {
            Name = "succeeded",
            Content = FlowContent.FromBytes(new byte[] { 2 })
        });

        (await ports.SendAsync(Input, failed)).IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Input, succeeded)).IsAccepted.ShouldBeTrue();
        var first = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);
        var second = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);

        first.Payload.Error.ShouldNotBeNull().Code.ShouldBe(SessionErrorCodeNames.RecordFailed);
        first.CorrelationId.ShouldBe(failed.CorrelationId);
        second.Payload.Value.ShouldNotBeNull().Name.ShouldBe("succeeded");
        second.CorrelationId.ShouldBe(succeeded.CorrelationId);
    }

    [Fact]
    public async Task Query_store_failure_is_output_data_and_later_messages_continue()
    {
        var store = new TestSessionStore { FailNextQuery = true };
        store.AddSession(new SessionMetadata
        {
            SessionId = "session-1",
            StartedAt = DateTimeOffset.Parse("2026-06-21T10:30:00Z")
        });
        await using var host = await StartHostAsync(
            SessionsCompositionNodeTypes.Query,
            Properties(("emitSessionsInResult", true)),
            store);
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var observed = (await ports.ObserveAsync<FlowResult<SessionQueryOutcome>>(Output))
            .Observation.ShouldNotBeNull();
        await using var observation = observed;
        var failed = FlowMessage.Create(new SessionQueryRequest());
        var succeeded = FlowMessage.Create(new SessionQueryRequest());

        (await ports.SendAsync(Input, failed)).IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Input, succeeded)).IsAccepted.ShouldBeTrue();
        var first = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);
        var second = await observation.Messages.ReceiveAsync().WaitAsync(Timeout);

        first.Payload.Error.ShouldNotBeNull().Code.ShouldBe(SessionErrorCodeNames.QueryFailed);
        first.CorrelationId.ShouldBe(failed.CorrelationId);
        second.Payload.Value.ShouldNotBeNull().Count.ShouldBe(1);
        second.CorrelationId.ShouldBe(succeeded.CorrelationId);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        ISessionStore? store = null,
        ISessionStoreFactory? factory = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        var resources = new List<string>();
        if (store is not null || factory is not null)
        {
            componentProperties[SessionsCompositionResourceNames.Store] = "Resources.sessions";
            resources.Add("sessions");
        }
        if (clock is not null)
        {
            componentProperties[SessionsCompositionResourceNames.Clock] = "Resources.fixed";
            resources.Add("fixed");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                componentType,
                componentProperties,
                resources,
                componentName: ComponentName),
            registry => RegisterAll(registry),
            configureRuntimeServices: context =>
            {
                if (store is not null)
                {
                    context.Services.AddExternalFluxFlowResource<ISessionStore>(
                        ApplicationAddress.Resource("sessions"),
                        store);
                }
                if (factory is not null)
                {
                    context.Services.AddExternalFluxFlowResource<ISessionStoreFactory>(
                        ApplicationAddress.Resource("sessions"),
                        factory);
                }
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("fixed"),
                        clock);
                }
            });
    }

    private static ValueTask<CanonicalApplicationTestHost> StartSourceHostAsync(
        IReadOnlyDictionary<string, object?> properties,
        ISessionStore store,
        TimeProvider clock,
        MessageTracker<FlowResult<SessionContentRecord>> tracker)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[SessionsCompositionResourceNames.Store] = "Resources.sessions";
        componentProperties[SessionsCompositionResourceNames.Clock] = "Resources.fixed";
        var definition = new ApplicationDefinition(
            [
                KeyValuePair.Create<string, ResourceDefinition>(
                    "sessions",
                    new ResourceInstanceDefinition("host.external")),
                KeyValuePair.Create<string, ResourceDefinition>(
                    "fixed",
                    new ResourceInstanceDefinition("host.external"))
            ],
            [KeyValuePair.Create(
                WorkflowName,
                new FluxFlow.Composition.Model.WorkflowDefinition([
                    KeyValuePair.Create(
                        ComponentName,
                        Component(SessionsCompositionNodeTypes.Replay, componentProperties)),
                    KeyValuePair.Create(
                        "recorder",
                        new ComponentDefinition(RecorderType))
                ]))]);

        return CanonicalApplicationTestHost.StartAsync(
            definition,
            registry =>
            {
                RegisterAll(registry);
                RegisterRecorder(registry, tracker);
            },
            configureRuntimeServices: context =>
            {
                context.Services.AddExternalFluxFlowResource<ISessionStore>(
                    ApplicationAddress.Resource("sessions"),
                    store);
                context.Services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    clock);
            });
    }

    private static ComponentDefinition Component(
        string componentType,
        IReadOnlyDictionary<string, object?> properties)
        => new(
            componentType,
            properties.Select(property => KeyValuePair.Create(
                property.Key,
                JsonSerializer.SerializeToElement(property.Value))));

    private static void RegisterRecorder(
        CompositionNodeRegistry registry,
        MessageTracker<FlowResult<SessionContentRecord>> tracker)
        => registry.Register(
            RecorderType,
            _ =>
            {
                var node = new MessageRecordingNode<FlowResult<SessionContentRecord>>(tracker);
                return ValueTask.FromResult(ComposedNode.Create(
                    node,
                    inputs: [CompositionPorts.Input("Input", node.Input)]));
            },
            inputs: [CompositionPorts.Metadata<FlowResult<SessionContentRecord>>("Input")]);

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
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

    private static void AssertTransformPorts(
        string inputType,
        string outputType,
        ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(SessionsCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(inputType);
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        AssertOutputPort(
            output,
            SessionsCompositionPortNames.Output,
            outputType,
            order: 1,
            isPrimary: true);
    }

    private static void AssertSourcePort(
        string outputType,
        ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(1);

        AssertOutputPort(
            metadata.Ports[0],
            SessionsCompositionPortNames.Output,
            outputType,
            order: 0,
            isPrimary: true);
    }

    private static void AssertQueryPorts(ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(2);

        metadata.Ports[0].Name.Value.ShouldBe(SessionsCompositionPortNames.Input);
        metadata.Ports[0].Direction.ShouldBe(PortDirection.Input);
        metadata.Ports[0].Order.ShouldBe(0);
        metadata.Ports[0].ValueType?.Value.ShouldBe(nameof(SessionQueryRequest));
        metadata.Ports[0].IsPrimary.ShouldBeTrue();

        AssertOutputPort(
            metadata.Ports[1],
            SessionsCompositionPortNames.Output,
            "FlowResult<SessionQueryOutcome>",
            order: 1,
            isPrimary: true);
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
        port.ValueType?.Value.ShouldBe(valueType);
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

    private static IReadOnlyDictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, ResourceDesignMetadata> ResourcesByName(
        ComponentDesignMetadata metadata)
        => metadata.Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

    private static void AssertResources(ComponentDesignMetadata metadata)
    {
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (SessionsCompositionResourceNames.Store, 0, true, $"{nameof(ISessionStore)} or {nameof(ISessionStoreFactory)}"),
            (SessionsCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
        ]);
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);

        if (editor is null)
        {
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
    }

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static async Task SeedContentRecordsAsync(
        TestSessionStore store,
        TimeProvider clock,
        string sessionId,
        params (DateTimeOffset Timestamp, string Name, byte[] Bytes)[] records)
    {
        await using var recorder = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = sessionId },
            store,
            clock);
        recorder.Output.LinkTo(
            DataflowBlock.NullTarget<FlowMessage<FlowResult<SessionContentRecord>>>());
        foreach (var record in records)
        {
            await recorder.Input.SendAsync(FlowMessage.Create(new SessionContentRecordInput
            {
                Timestamp = record.Timestamp,
                Name = record.Name,
                Content = FlowContent.FromBytes(record.Bytes, "application/octet-stream")
            })).WaitAsync(Timeout);
        }

        recorder.Complete();
        await recorder.Completion.WaitAsync(Timeout);
        await recorder.SessionCompleted.WaitAsync(Timeout);
    }

    private sealed class MessageTracker<T>(int expectedCount)
    {
        private readonly object _gate = new();
        private readonly List<FlowMessage<T>> _messages = [];
        private readonly TaskCompletionSource<IReadOnlyList<FlowMessage<T>>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<FlowMessage<T>>> Completion => _completion.Task;

        public void Record(FlowMessage<T> message)
        {
            lock (_gate)
            {
                _messages.Add(message);
                if (_messages.Count == expectedCount)
                    _completion.TrySetResult(_messages.ToArray());
            }
        }
    }

    private sealed class MessageRecordingNode<T> : IFlowNode
    {
        private readonly ActionBlock<FlowMessage<T>> _input;

        public MessageRecordingNode(MessageTracker<T> tracker)
        {
            ArgumentNullException.ThrowIfNull(tracker);
            _input = new ActionBlock<FlowMessage<T>>(tracker.Record);
        }

        public ITargetBlock<FlowMessage<T>> Input => _input;

        public Task Completion => _input.Completion;

        public void Complete() => _input.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);

        public ValueTask DisposeAsync()
        {
            _input.Complete();
            return ValueTask.CompletedTask;
        }
    }

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
        public SessionQueryRequest? LastQuery { get; private set; }

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

            LastQuery = request;

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
