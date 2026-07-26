using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationRuntimeSignalsTests
{
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("Main", "Sink", "Input");
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Main", "Source", "Output");

    [Fact]
    public void Signal_payload_json_contracts_are_stable()
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
        var error = new DataFlowError(
            "link.condition.failed",
            "Condition failed.",
            "link");
        var systemEvent = new ApplicationSystemEvent
        {
            Timestamp = timestamp,
            Name = ApplicationSystemEventNames.LinkConditionFailed,
            Category = ApplicationSystemEventCategory.Link,
            Subject = "Main.Source.Output",
            Error = error,
            Details = JsonSerializer.SerializeToElement(new
            {
                port = "Main.Source.Output"
            })
        };
        var diagnostic = new ApplicationDiagnostic
        {
            Timestamp = timestamp,
            Name = ApplicationDiagnosticNames.RequestCompleted,
            Kind = ApplicationDiagnosticKind.Timing,
            Level = ApplicationDiagnosticLevel.Information,
            Subject = "Main.Source.Output",
            Message = "Request completed.",
            Duration = TimeSpan.FromMilliseconds(125),
            Measurement = 125,
            Unit = "ms",
            Error = error,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = "received"
            }
        };

        JsonSerializer.Serialize(systemEvent).ShouldBe(
            "{\"Timestamp\":\"2026-07-17T01:02:03+00:00\"," +
            "\"Name\":\"flow.link.condition.failed\",\"Category\":3," +
            "\"Subject\":\"Main.Source.Output\",\"Error\":{" +
            "\"code\":\"link.condition.failed\",\"message\":\"Condition failed.\"," +
            "\"category\":\"link\",\"isTransient\":false," +
            "\"details\":null}," +
            "\"Details\":{\"port\":\"Main.Source.Output\"}}");
        JsonSerializer.Serialize(diagnostic).ShouldBe(
            "{\"Timestamp\":\"2026-07-17T01:02:03+00:00\"," +
            "\"Name\":\"flow.port.request.completed\",\"Kind\":4,\"Level\":2," +
            "\"Subject\":\"Main.Source.Output\",\"Message\":\"Request completed.\"," +
            "\"Duration\":\"00:00:00.1250000\",\"Measurement\":125," +
            "\"Unit\":\"ms\",\"Error\":{" +
            "\"code\":\"link.condition.failed\",\"message\":\"Condition failed.\"," +
            "\"category\":\"link\",\"isTransient\":false," +
            "\"details\":null}," +
            "\"Attributes\":{\"status\":\"received\"}}");
    }

    [Fact]
    public async Task Builder_registers_reserved_system_outputs_with_exact_compiler_metadata()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();

        var eventPort = runtime.Ports.Single(port => port.Address == ApplicationAddress.SystemEvents);
        eventPort.Direction.ShouldBe(ApplicationPortDirection.Output);
        eventPort.PayloadType.ShouldBe(typeof(ApplicationSystemEvent));
        eventPort.Capacity.ShouldBe(ApplicationPortRuntimeBuilder.DefaultSystemOutputCapacity);
        var diagnosticPort = runtime.Ports.Single(port => port.Address == ApplicationAddress.SystemDiagnostics);
        diagnosticPort.PayloadType.ShouldBe(typeof(ApplicationDiagnostic));

        ApplicationPortRuntimeBuilder.SystemOutputs.ShouldContain(metadata =>
            metadata.Address == ApplicationAddress.SystemEvents &&
            metadata.MessageType == typeof(ApplicationSystemEvent));
        ApplicationPortRuntimeBuilder.SystemOutputs.ShouldContain(metadata =>
            metadata.Address == ApplicationAddress.SystemDiagnostics &&
            metadata.MessageType == typeof(ApplicationDiagnostic));
        Should.Throw<ArgumentException>(() => new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(ApplicationAddress.SystemEvents));
    }

    [Fact]
    public async Task System_event_publication_is_ordered_and_backpressures_when_full()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();
        var slow = new PostponedTarget<FlowMessage<ApplicationSystemEvent>>();
        using var link = runtime.SystemEvents.LinkTo(slow);

        (await runtime.PublishSystemEventAsync(EventMessage("event-0")))
            .IsAccepted.ShouldBeTrue();
        await slow.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        for (var index = 1; index <= ApplicationPortRuntimeBuilder.DefaultSystemOutputCapacity; index++)
        {
            (await runtime.PublishSystemEventAsync(EventMessage($"event-{index}")))
                .IsAccepted.ShouldBeTrue();
        }

        var blocked = runtime.PublishSystemEventAsync(EventMessage("event-blocked")).AsTask();
        await Task.Delay(50);
        blocked.IsCompleted.ShouldBeFalse();

        slow.AcceptPostponed();

        (await blocked.WaitAsync(TimeSpan.FromSeconds(5))).IsAccepted.ShouldBeTrue();
        slow.Accepted.Single().Value.Name.ShouldBe("event-0");
    }

    [Fact]
    public async Task Diagnostics_reject_overflow_promptly_and_preserve_accepted_order()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();
        var slow = new PostponedTarget<FlowMessage<ApplicationDiagnostic>>();
        using var slowLink = runtime.Diagnostics.LinkTo(slow);

        runtime.TryPublishDiagnostic(DiagnosticMessage("diagnostic-0")).ShouldBeTrue();
        await slow.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        for (var index = 1; index <= ApplicationPortRuntimeBuilder.DefaultSystemOutputCapacity; index++)
            runtime.TryPublishDiagnostic(DiagnosticMessage($"diagnostic-{index}")).ShouldBeTrue();

        var startedAt = Stopwatch.GetTimestamp();
        runtime.TryPublishDiagnostic(DiagnosticMessage("overflow")).ShouldBeFalse();
        Stopwatch.GetElapsedTime(startedAt).ShouldBeLessThan(TimeSpan.FromSeconds(1));
        slow.AcceptPostponed();
        slow.Accepted.Single().Value.Name.ShouldBe("diagnostic-0");

        slowLink.Dispose();
        await using var orderedRuntime = new ApplicationPortRuntimeBuilder().Build();
        var ordered = new BufferBlock<FlowMessage<ApplicationDiagnostic>>();
        using var orderedLink = orderedRuntime.Diagnostics.LinkTo(ordered);
        var names = new[] { "ordered-1", "ordered-2", "ordered-3" };
        foreach (var name in names)
            orderedRuntime.TryPublishDiagnostic(DiagnosticMessage(name)).ShouldBeTrue();

        var received = new List<string>();
        foreach (var _ in names)
            received.Add((await ordered.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.Name);
        received.ShouldBe(names);
    }

    [Fact]
    public async Task System_events_flow_through_canonical_links_without_stealing_host_subscriptions()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<ApplicationSystemEvent>(Input)
            .Build();
        var sink = new BufferBlock<FlowMessage<ApplicationSystemEvent>>();
        await using var sinkAttachment = await runtime.AttachInputAsync(Input, sink);
        using var route = runtime.Connect(CompileSystemEventLink());
        var hostReceive = runtime.ReceiveAsync<ApplicationSystemEvent>(
            ApplicationAddress.SystemEvents,
            TimeSpan.FromSeconds(5));
        var message = EventMessage("workflow-event");

        (await runtime.PublishSystemEventAsync(message)).IsAccepted.ShouldBeTrue();

        (await hostReceive).Message!.ShouldBeSameAs(message);
        (await sink.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Link_condition_failure_emits_a_trace_correlated_system_event_and_keeps_runtime_active()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .AddInput<string>(Input)
            .Build();
        var sink = new BufferBlock<FlowMessage<string>>();
        await using var sinkAttachment = await runtime.AttachInputAsync(Input, sink);
        using var route = runtime.Connect(CompileFailingLink());
        var source = new BufferBlock<FlowMessage<string>>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var receive = runtime.ReceiveAsync<ApplicationSystemEvent>(
            ApplicationAddress.SystemEvents,
            TimeSpan.FromSeconds(5));
        var message = FlowMessage.Create("payload");

        source.Post(message).ShouldBeTrue();

        var systemEvent = (await receive).Message!;
        systemEvent.Value.Name.ShouldBe(ApplicationSystemEventNames.LinkConditionFailed);
        systemEvent.Value.Category.ShouldBe(ApplicationSystemEventCategory.Link);
        systemEvent.Value.Error.ShouldNotBeNull();
        systemEvent.TraceId.ShouldBe(message.TraceId);
        systemEvent.CausationId.ShouldBe(message.MessageId);
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Active);
        sink.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Source_fault_emits_component_event_and_only_makes_that_output_unavailable()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .Build();
        var source = new BufferBlock<FlowMessage<string>>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var receive = runtime.ReceiveAsync<ApplicationSystemEvent>(
            ApplicationAddress.SystemEvents,
            TimeSpan.FromSeconds(5));

        ((IDataflowBlock)source).Fault(new InvalidOperationException("source failed"));
        await Should.ThrowAsync<InvalidOperationException>(async () => await source.Completion);

        var systemEvent = (await receive).Message!;
        systemEvent.Value.Name.ShouldBe(ApplicationSystemEventNames.ComponentFaulted);
        systemEvent.Value.Category.ShouldBe(ApplicationSystemEventCategory.Component);
        await EventuallyAsync(() => runtime.Status.Ports
            .Single(port => port.Address == Output)
            .Availability == ApplicationPortAvailability.Unavailable);
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Active);
    }

    [Fact]
    public async Task Idle_input_component_fault_becomes_unavailable_without_faulting_runtime()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .Build();
        var target = new BufferBlock<FlowMessage<string>>();
        await using var attachment = await runtime.AttachInputAsync(Input, target);
        var receive = runtime.ReceiveAsync<ApplicationSystemEvent>(
            ApplicationAddress.SystemEvents,
            TimeSpan.FromSeconds(5));

        ((IDataflowBlock)target).Fault(new InvalidOperationException("target failed"));
        await Should.ThrowAsync<InvalidOperationException>(async () => await target.Completion);

        var systemEvent = (await receive).Message!;
        systemEvent.Value.Name.ShouldBe(ApplicationSystemEventNames.ComponentFaulted);
        systemEvent.Value.Category.ShouldBe(ApplicationSystemEventCategory.Component);
        runtime.Status.Ports.Single(port => port.Address == Input)
            .Availability.ShouldBe(ApplicationPortAvailability.Unavailable);
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Active);
    }

    [Fact]
    public async Task Status_snapshots_track_port_availability_and_terminal_state()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .AddOutput<string>(Output)
            .Build();
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Active);
        runtime.Status.Ports.Single(port => port.Address == Input)
            .Availability.ShouldBe(ApplicationPortAvailability.Unavailable);
        runtime.Status.Ports.Single(port => port.Address == ApplicationAddress.SystemEvents)
            .Availability.ShouldBe(ApplicationPortAvailability.Available);

        var input = new BufferBlock<FlowMessage<string>>();
        var output = new BufferBlock<FlowMessage<string>>();
        await using var inputAttachment = await runtime.AttachInputAsync(Input, input);
        using var outputAttachment = runtime.AttachOutput(Output, output);
        runtime.Status.Ports.Single(port => port.Address == Input)
            .Availability.ShouldBe(ApplicationPortAvailability.Available);
        runtime.Status.Ports.Single(port => port.Address == Output)
            .Availability.ShouldBe(ApplicationPortAvailability.Available);
        var lifecycle = runtime.ReceiveAsync<ApplicationSystemEvent>(
            ApplicationAddress.SystemEvents,
            TimeSpan.FromSeconds(5));

        runtime.Complete();
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Completing);
        (await lifecycle).Message!.Value.Name.ShouldBe(
            ApplicationSystemEventNames.RuntimeCompleting);
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Completed);
        runtime.Status.Ports.ShouldAllBe(port =>
            port.Availability == ApplicationPortAvailability.Completed);
    }

    [Fact]
    public async Task Port_activity_and_request_timing_are_emitted_as_trace_correlated_diagnostics()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .AddOutput<string>(Output)
            .Build();
        var responses = new BufferBlock<FlowMessage<string>>();
        using var outputAttachment = runtime.AttachOutput(Output, responses);
        var processor = new ActionBlock<FlowMessage<string>>(request =>
            responses.Post(request.With("response")));
        await using var inputAttachment = await runtime.AttachInputAsync(Input, processor);
        var diagnostics = new BufferBlock<FlowMessage<ApplicationDiagnostic>>();
        using var diagnosticsLink = runtime.Diagnostics.LinkTo(diagnostics);
        var request = FlowMessage.Create("request");

        var result = await runtime.SendAndReceiveAsync<string, string>(
            Input,
            Output,
            request,
            TimeSpan.FromSeconds(5));

        result.Status.ShouldBe(PortRequestStatus.Received);
        var received = new List<FlowMessage<ApplicationDiagnostic>>();
        while (received.Select(message => message.Value.Name).Distinct().Count() < 3)
        {
            received.Add(await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }

        received.ShouldContain(message =>
            message.Value.Name == ApplicationDiagnosticNames.InputAccepted &&
            message.TraceId == request.TraceId);
        received.ShouldContain(message =>
            message.Value.Name == ApplicationDiagnosticNames.OutputEmitted &&
            message.TraceId == request.TraceId);
        received.ShouldContain(message =>
            message.Value.Name == ApplicationDiagnosticNames.RequestCompleted &&
            message.Value.Duration > TimeSpan.Zero &&
            message.TraceId == request.TraceId);
    }

    [Fact]
    public async Task Diagnostic_system_link_rejection_is_reported_once_without_recursive_diagnostics()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<ApplicationDiagnostic>(Input, capacity: 1)
            .Build();
        var target = new PostponedTarget<FlowMessage<ApplicationDiagnostic>>();
        await using var targetAttachment = await runtime.AttachInputAsync(Input, target);
        using var route = runtime.Connect(CompileSystemDiagnosticLink());

        runtime.TryPublishDiagnostic(DiagnosticMessage("first")).ShouldBeTrue();
        await target.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.TryPublishDiagnostic(DiagnosticMessage("second")).ShouldBeTrue();

        var rejection = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        rejection.Reason.ShouldBe(ApplicationPortRejectionReason.Full);
        rejection.RelatedPort.ShouldBe(ApplicationAddress.SystemDiagnostics);
        await Task.Delay(100);
        ((IReceivableSourceBlock<ApplicationPortRejection>)runtime.Rejections)
            .TryReceive(out _)
            .ShouldBeFalse();
        target.AcceptPostponed();
    }

    [Fact]
    public async Task Accepted_diagnostics_integrate_with_logging_activities_metrics_and_diagnostic_source()
    {
        var logger = new RecordingLogger();
        var activities = new ConcurrentQueue<string>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationRuntimeInstrumentation.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Enqueue(activity.OperationName)
        };
        ActivitySource.AddActivityListener(activityListener);

        var diagnosticEvents = new ConcurrentQueue<string>();
        using var allListeners = DiagnosticListener.AllListeners.Subscribe(
            new Observer<DiagnosticListener>(listener =>
            {
                if (listener.Name == ApplicationRuntimeInstrumentation.DiagnosticSourceName)
                {
                    listener.Subscribe(new Observer<KeyValuePair<string, object?>>(
                        item => diagnosticEvents.Enqueue(item.Key)));
                }
            }));

        var measurements = new ConcurrentQueue<string>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationRuntimeInstrumentation.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, _, _, _) => measurements.Enqueue(instrument.Name));
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, _, _, _) => measurements.Enqueue(instrument.Name));
        meterListener.Start();

        await using var runtime = new ApplicationPortRuntimeBuilder()
            .UseLogger(logger)
            .Build();
        var message = DiagnosticMessage(
            "custom.timing",
            ApplicationDiagnosticKind.Timing,
            measurement: 12.5);

        runtime.TryPublishDiagnostic(message).ShouldBeTrue();

        logger.Messages.ShouldContain(message => message.Contains("custom.timing", StringComparison.Ordinal));
        activities.ShouldContain("custom.timing");
        diagnosticEvents.ShouldContain(ApplicationRuntimeInstrumentation.DiagnosticEventName);
        measurements.ShouldContain("fluxflow.runtime.diagnostics.accepted");
        measurements.ShouldContain("fluxflow.runtime.diagnostic.measurement");
    }

    [Fact]
    public async Task Throwing_host_meter_listener_cannot_fault_diagnostic_publication()
    {
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationRuntimeInstrumentation.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>(
            static (_, _, _, _) => throw new InvalidOperationException("listener failed"));
        meterListener.Start();
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();

        runtime.TryPublishDiagnostic(DiagnosticMessage("isolated-listener")).ShouldBeTrue();
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Active);
    }

    [Fact]
    public async Task Signal_publication_honors_cancellation_completion_and_subscriber_failure_isolation()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();
        using var failedLink = runtime.SystemEvents.LinkTo(
            new ThrowingTarget<FlowMessage<ApplicationSystemEvent>>());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.PublishSystemEventAsync(EventMessage("canceled"), canceled.Token));
        (await runtime.PublishSystemEventAsync(EventMessage("detaches-failed-subscriber")))
            .IsAccepted.ShouldBeTrue();
        (await runtime.PublishSystemEventAsync(EventMessage("healthy-after-failure")))
            .IsAccepted.ShouldBeTrue();

        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await runtime.PublishSystemEventAsync(EventMessage("late")))
            .Status.ShouldBe(SystemEventPublishStatus.Completed);
    }

    [Fact]
    public async Task Throwing_subscriber_completion_is_isolated_from_runtime_completion()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();
        using var failedLink = runtime.SystemEvents.LinkTo(
            new ThrowingCompletionTarget<FlowMessage<ApplicationSystemEvent>>(),
            new DataflowLinkOptions { PropagateCompletion = true });

        runtime.Complete();

        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Completed);
    }

    private static FlowMessage<ApplicationSystemEvent> EventMessage(string name)
        => FlowMessage.Create(new ApplicationSystemEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = name,
            Category = ApplicationSystemEventCategory.Lifecycle,
            Subject = "test"
        });

    private static FlowMessage<ApplicationDiagnostic> DiagnosticMessage(
        string name,
        ApplicationDiagnosticKind kind = ApplicationDiagnosticKind.Log,
        double? measurement = null)
        => FlowMessage.Create(new ApplicationDiagnostic
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = name,
            Kind = kind,
            Level = ApplicationDiagnosticLevel.Information,
            Message = name,
            Measurement = measurement,
            Unit = measurement is null ? null : "ms"
        });

    private static CompiledApplicationLink CompileSystemEventLink()
    {
        var definition = ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Sink": {
                    "Type": "sink",
                    "Input": "System.Events.Output"
                  }
                }
              }
            }
            """);
        var registry = new CompositionNodeRegistry().Register(
            "sink",
            UnusedFactory,
            inputs: [CompositionPorts.Metadata<ApplicationSystemEvent>("Input")]);
        var result = new ApplicationLinkCompiler(
            registry,
            systemOutputs: ApplicationPortRuntimeBuilder.SystemOutputs)
            .Compile(definition);
        result.IsValid.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        return result.Links.Single();
    }

    private static CompiledApplicationLink CompileFailingLink()
    {
        var definition = ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": {
                    "Type": "source",
                    "Output": { "Port": "Sink.Input", "Condition": "fail" }
                  },
                  "Sink": { "Type": "sink" }
                }
              }
            }
            """);
        var registry = new CompositionNodeRegistry()
            .Register(
                "source",
                UnusedFactory,
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "sink",
                UnusedFactory,
                inputs: [CompositionPorts.Metadata<string>("Input")]);
        var result = new ApplicationLinkCompiler(registry, new FailingExpressionEngine())
            .Compile(definition);
        result.IsValid.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        return result.Links.Single();
    }

    private static CompiledApplicationLink CompileSystemDiagnosticLink()
    {
        var definition = ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Sink": {
                    "Type": "sink",
                    "Input": "System.Diagnostics.Output"
                  }
                }
              }
            }
            """);
        var registry = new CompositionNodeRegistry().Register(
            "sink",
            UnusedFactory,
            inputs: [CompositionPorts.Metadata<ApplicationDiagnostic>("Input")]);
        var result = new ApplicationLinkCompiler(
            registry,
            systemOutputs: ApplicationPortRuntimeBuilder.SystemOutputs)
            .Compile(definition);
        result.IsValid.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        return result.Links.Single();
    }

    private static ValueTask<ComposedNode> UnusedFactory(CompositionNodeFactoryContext _)
        => throw new InvalidOperationException("Link compilation must not activate node factories.");

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not reached before the test timeout.");
    }

    private sealed class FailingExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "test";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("Compiled expressions are required.");

        public IFlowCompiledExpression<T> Compile<T>(string expression)
            => (IFlowCompiledExpression<T>)(object)new FailingExpression();
    }

    private sealed class FailingExpression : IFlowCompiledExpression<bool>
    {
        public bool Evaluate(FlowMapContext context)
            => throw new InvalidOperationException("Condition failed.");
    }

    private sealed class PostponedTarget<T> : ITargetBlock<T>
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _offered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ISourceBlock<T>? _source;
        private DataflowMessageHeader _header;

        public List<T> Accepted { get; } = [];

        public Task Offered => _offered.Task;

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
        {
            lock (_gate)
            {
                if (_completion.Task.IsCompleted)
                    return DataflowMessageStatus.DecliningPermanently;
                if (_source is not null)
                    return DataflowMessageStatus.Postponed;
                if (source is null)
                    return DataflowMessageStatus.Declined;

                _header = messageHeader;
                _source = source;
                _offered.TrySetResult();
                return DataflowMessageStatus.Postponed;
            }
        }

        public void AcceptPostponed()
        {
            ISourceBlock<T> source;
            DataflowMessageHeader header;
            lock (_gate)
            {
                source = _source.ShouldNotBeNull();
                header = _header;
                _source = null;
            }

            var value = source.ConsumeMessage(header, this, out var consumed);
            consumed.ShouldBeTrue();
            Accepted.Add(value!);
        }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class ThrowingTarget<T> : ITargetBlock<T>
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
            => throw new InvalidOperationException("Subscriber failed.");

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class ThrowingCompletionTarget<T> : ITargetBlock<T>
    {
        public Task Completion => Task.CompletedTask;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
            => DataflowMessageStatus.Accepted;

        public void Complete() => throw new InvalidOperationException("Subscriber completion failed.");

        public void Fault(Exception exception)
            => throw new InvalidOperationException("Subscriber fault propagation failed.");
    }

    private sealed class RecordingLogger : ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Enqueue(formatter(state, exception));
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => onNext(value);
    }
}
