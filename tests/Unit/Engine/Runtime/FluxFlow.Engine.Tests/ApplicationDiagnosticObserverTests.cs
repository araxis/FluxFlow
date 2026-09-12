using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

[Collection(RuntimeMetricsCollection.Name)]
public sealed class ApplicationDiagnosticObserverTests
{
    private const string Accepted = "fluxflow.runtime.diagnostics.accepted";
    private const string Duration = "fluxflow.runtime.request.duration";
    private const string SystemAccepted = "fluxflow.runtime.system_events.accepted";
    private const string SystemRejected = "fluxflow.runtime.system_events.rejected";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Typed_diagnostic_preserves_canonical_snapshot_and_context()
    {
        using var traces = new TraceProbe();
        using var observer = new ObserverProbe();
        using var current = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var diagnostics = new BufferBlock<FlowMessage>();
        var activity = new BufferBlock<FlowMessage>();
        using var diagnosticLink = runtime.Diagnostics.LinkTo(diagnostics);
        using var activityLink = runtime.Activity.LinkTo(activity);
        var request = FlowMessage.Create(FlowValue.From("request"), correlationId: CorrelationId.New());
        var timestamp = new DateTimeOffset(2026, 9, 11, 10, 20, 30, TimeSpan.Zero);
        var measurements = new Dictionary<string, double> { ["duration.ms"] = 12.5 };
        var diagnostic = new FlowEvent
        {
            Name = $"  {ApplicationDiagnosticNames.RequestCompleted}  ",
            Timestamp = timestamp,
            Level = FlowEventLevel.Warning,
            Message = "request done",
            Dimensions = new Dictionary<string, object?> { ["status"] = "Received" },
            Attributes = new Dictionary<string, object?> { ["attempt"] = 2 },
            Measurements = measurements,
            Details = FlowValue.From(new { result = "ok" })
        };
        FlowMessage? observed = null;
        runtime.EventObserver = message =>
        {
            observed = message;
            measurements["duration.ms"] = 99;
        };

        runtime.TryPublishDiagnostic(diagnostic, request.CorrelationId, request.TraceId,
            request.MessageId, ApplicationAddress.WorkflowPort("Main", "Sink", "Input")).ShouldBeTrue();
        var received = await diagnostics.ReceiveAsync(Deadline);
        received.ShouldBeSameAs(observed);
        (await activity.ReceiveAsync(Deadline)).ShouldBeSameAs(received);
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        received.Timestamp.ShouldBe(timestamp);
        received.TraceId.ShouldBe(request.TraceId);
        received.CorrelationId.ShouldBe(request.CorrelationId);
        received.CausationId.ShouldBe(request.MessageId);
        received.MessageId.ShouldNotBe(request.MessageId);
        received.Headers[FlowEventHeaders.Source].ShouldBe("Main.Sink.Input");
        received.Headers[FlowEventHeaders.Workflow].ShouldBe("Main");
        received.Headers[FlowEventHeaders.Component].ShouldBe("Sink");
        received.Headers[FlowEventHeaders.Type].ShouldBe(ApplicationDiagnosticNames.RequestCompleted);
        var body = received.Value!.ToJsonElement();
        body.GetProperty("type").GetString().ShouldBe(ApplicationDiagnosticNames.RequestCompleted);
        body.GetProperty("kind").GetString().ShouldBe("diagnostic");
        body.GetProperty("level").GetString().ShouldBe("warning");
        body.GetProperty("timestamp").GetDateTimeOffset().ShouldBe(timestamp);
        body.GetProperty("measurements").GetProperty("duration.ms").GetDouble().ShouldBe(12.5);
        body.GetProperty("dimensions").GetProperty("status").GetString().ShouldBe("Received");
        body.GetProperty("attributes").GetProperty("attempt").GetInt32().ShouldBe(2);
        body.GetProperty("details").GetProperty("result").GetString().ShouldBe("ok");
        measurements["duration.ms"].ShouldBe(99);
        observer.Metrics.Select(metric => metric.Name).ShouldBe([Accepted, Duration]);
        observer.Metrics.Select(metric => metric.Value).ShouldBe([1d, 12.5]);
        var log = observer.Logger.Entries.ShouldHaveSingleItem();
        log.Event.Name.ShouldBe(ApplicationDiagnosticNames.RequestCompleted);
        log.Text.ShouldBe($"{ApplicationDiagnosticNames.RequestCompleted}: request done");
        log.Level.ShouldBe(LogLevel.Warning);
        log.Context.ShouldBeSameAs(current);
        observer.SourceValues.ShouldHaveSingleItem().Value.ShouldBeSameAs(received);
        observer.SourceContexts.ShouldHaveSingleItem().ShouldBeSameAs(current);
        foreach (var metric in observer.Metrics) metric.Context.ShouldBeSameAs(current);
        traces.Started.ShouldBeEmpty();
        traces.Stopped.ShouldBeEmpty();
        Activity.Current.ShouldBeSameAs(current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_diagnostic_validates_before_observation_even_when_stopped(bool stopped)
    {
        using var observer = new ObserverProbe();
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var observed = new List<FlowMessage>();
        runtime.EventObserver = observed.Add;
        if (stopped) runtime.Complete();

        Should.Throw<ArgumentNullException>(() => runtime.TryPublishDiagnostic((FlowEvent)null!))
            .ParamName.ShouldBe("diagnostic");
        Should.Throw<ArgumentException>(() => runtime.TryPublishDiagnostic(new FlowEvent { Name = "  " }));
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Should.Throw<ArgumentOutOfRangeException>(() => runtime.TryPublishDiagnostic(new FlowEvent
            {
                Name = "invalid.measurement",
                Measurements = new Dictionary<string, double> { ["duration.ms"] = invalid }
            }));
        Should.Throw<ArgumentException>(() => runtime.TryPublishDiagnostic(new FlowEvent
        {
            Name = "invalid.key",
            Measurements = new Dictionary<string, double> { [" "] = 1 }
        }));
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        observed.ShouldBeEmpty();
        observer.Metrics.ShouldBeEmpty();
        observer.SourceEnabledCalls.ShouldBe(0);
        observer.SourceValues.ShouldBeEmpty();
        observer.Logger.EnabledCalls.ShouldBe(0);
        observer.Logger.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_diagnostic_observes_before_stop_and_propagates_observer_failure(bool throwObserver)
    {
        using var observer = new ObserverProbe();
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var diagnostics = new BufferBlock<FlowMessage>();
        var activity = new BufferBlock<FlowMessage>();
        using var diagnosticLink = runtime.Diagnostics.LinkTo(diagnostics);
        using var activityLink = runtime.Activity.LinkTo(activity);
        var observed = new List<FlowMessage>();
        var failure = new InvalidOperationException("event observer failed");
        runtime.EventObserver = message =>
        {
            observed.Add(message);
            if (throwObserver) throw failure;
        };
        var diagnostic = new FlowEvent { Name = "test.terminal" };
        if (throwObserver)
            Should.Throw<InvalidOperationException>(() => runtime.TryPublishDiagnostic(diagnostic)).ShouldBeSameAs(failure);
        else
        {
            runtime.Complete();
            runtime.TryPublishDiagnostic(diagnostic).ShouldBeFalse();
        }
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        observed.ShouldHaveSingleItem().ToFlowEvent().Name.ShouldBe("test.terminal");
        diagnostics.Count.ShouldBe(0);
        activity.Count.ShouldBe(0);
        observer.SourceValues.ShouldBeEmpty();
        observer.Logger.Entries.ShouldBeEmpty();
        observer.Metrics.Count.ShouldBe(throwObserver ? 0 : 1);
        if (!throwObserver)
        {
            var dropped = observer.Metrics.ShouldHaveSingleItem();
            dropped.Name.ShouldBe("fluxflow.runtime.diagnostics.dropped");
            dropped.Value.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("Input")]
    [InlineData("Output")]
    [InlineData("Timing")]
    [InlineData("Trace")]
    [InlineData("Log")]
    [InlineData("custom")]
    public async Task Diagnostic_publication_keeps_current_context_without_creating_spans(string kind)
    {
        using var traces = new TraceProbe();
        using var observer = new ObserverProbe();
        using var current = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = runtime.Diagnostics.LinkTo(sink);
        var message = new FlowEvent
        {
            Name = "test.diagnostic",
            Message = "original diagnostic",
            Dimensions = new Dictionary<string, object?> { ["kind"] = kind }
        }.ToMessage();

        runtime.TryPublishDiagnostic(message).ShouldBeTrue();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        traces.Started.ShouldBeEmpty();
        traces.Stopped.ShouldBeEmpty();
        observer.SourceValues.ShouldHaveSingleItem().Value.ShouldBeSameAs(message);
        observer.SourceContexts.ShouldHaveSingleItem().ShouldBeSameAs(current);
        var log = observer.Logger.Entries.ShouldHaveSingleItem();
        log.Context.ShouldBeSameAs(current);
        log.Text.ShouldBe("test.diagnostic: original diagnostic");
        observer.Metrics.ShouldHaveSingleItem().Context.ShouldBeSameAs(current);
        Activity.Current.ShouldBeSameAs(current);
    }

    [Fact]
    public async Task Processing_spans_remain_real_when_diagnostic_spans_are_removed()
    {
        using var traces = new TraceProbe();
        using var observer = new ObserverProbe();
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new ProcessingNode(async message =>
        {
            entered.TrySetResult(runtime.TryPublishDiagnostic(Diagnostic(0)));
            await release.Task;
            return message.With(FlowValue.From(8));
        });
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = node.Output.LinkTo(sink);
        using var producer = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        var input = FlowMessage.Create(FlowValue.From(7));
        try
        {
            (await node.Input.SendAsync(input)).ShouldBeTrue();
            (await entered.Task.WaitAsync(Deadline)).ShouldBeTrue();
            var span = traces.Started.ShouldHaveSingleItem();
            span.Source.Name.ShouldBe("FluxFlow.Core");
            span.OperationName.ShouldBe("flow.process");
            span.ParentSpanId.ShouldBe(producer.SpanId);
            span.TraceId.ShouldBe(producer.TraceId);
            span.Duration.ShouldBe(TimeSpan.Zero);
            traces.Stopped.ShouldBeEmpty();
            observer.Logger.Entries.ShouldHaveSingleItem().Context.ShouldBeSameAs(span);
            observer.SourceContexts.ShouldHaveSingleItem().ShouldBeSameAs(span);

            release.TrySetResult();
            var output = await sink.ReceiveAsync(Deadline);
            node.Complete();
            await node.Completion.WaitAsync(Deadline);
            output.Value!.Deserialize<int>().ShouldBe(8);
            output.CausationId.ShouldBe(input.MessageId);
            output.Headers["traceparent"].ShouldBe(span.Id);
            traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(span);
            span.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            Activity.Current.ShouldBeSameAs(producer);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("counter", false)]
    [InlineData("duration", false)]
    [InlineData("logger.enabled", false)]
    [InlineData("logger.write", false)]
    [InlineData("source.enabled", false)]
    [InlineData("source.write", false)]
    [InlineData("none", true)]
    [InlineData("counter", true)]
    [InlineData("duration", true)]
    [InlineData("logger.enabled", true)]
    [InlineData("logger.write", true)]
    [InlineData("source.enabled", true)]
    [InlineData("source.write", true)]
    public async Task Throwing_diagnostic_provider_does_not_suppress_other_channels(string failure, bool typed)
    {
        using var observer = new ObserverProbe(failure);
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var diagnostics = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var activity = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var diagnosticLink = runtime.Diagnostics.LinkTo(diagnostics);
        using var activityLink = runtime.Activity.LinkTo(activity);
        var messages = new[] { Diagnostic(0), Diagnostic(1) };
        FlowMessage? published = null;
        runtime.EventObserver = message => published = message;

        for (var index = 0; index < messages.Length; index++)
        {
            var message = messages[index];
            if (typed)
            {
                runtime.TryPublishDiagnostic(message.ToFlowEvent()).ShouldBeTrue();
                messages[index] = message = published.ShouldNotBeNull();
            }
            else runtime.TryPublishDiagnostic(message).ShouldBeTrue();
            (await diagnostics.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            (await activity.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        }
        runtime.Completion.IsCompleted.ShouldBeFalse();
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        observer.Metrics.Where(value => value.Name == Accepted).Select(value => value.Value).ShouldBe([1d, 1d]);
        observer.Metrics.Where(value => value.Name == Duration).Select(value => value.Value).ShouldBe([0.5, 1.5]);
        observer.Metrics.Count.ShouldBe(4);
        observer.Logger.EnabledCalls.ShouldBe(2);
        observer.Logger.Entries.Count.ShouldBe(failure == "logger.enabled" ? 0 : 2);
        observer.SourceEnabledCalls.ShouldBe(2);
        observer.SourceValues.Count.ShouldBe(failure == "source.enabled" ? 0 : 2);
        foreach (var log in observer.Logger.Entries)
        {
            log.Level.ShouldBe(LogLevel.Warning);
            log.Event.Name.ShouldBe(ApplicationDiagnosticNames.RequestCompleted);
        }
        if (failure != "source.enabled")
        {
            observer.SourceValues.Select(value => value.Key).ShouldBe(
                new[] { ApplicationRuntimeInstrumentation.DiagnosticEventName, ApplicationRuntimeInstrumentation.DiagnosticEventName });
            var observed = observer.SourceValues.ToArray();
            observed[0].Value.ShouldBeSameAs(messages[0]);
            observed[1].Value.ShouldBeSameAs(messages[1]);
        }
        runtime.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        Activity.Current.ShouldBeNull();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("counter")]
    [InlineData("source.enabled")]
    [InlineData("source.write")]
    public async Task System_event_provider_failure_preserves_other_observation_and_order(string failure)
    {
        using var observer = new ObserverProbe(failure);
        await using var runtime = new ApplicationRuntimeSignals(null);
        var events = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var activity = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var eventLink = runtime.SystemEvents.LinkTo(events);
        using var activityLink = runtime.Activity.LinkTo(activity);
        var messages = new[]
        {
            new FlowEvent { Name = "event.first", Kind = FlowEventKind.Lifecycle }.ToMessage(),
            new FlowEvent { Name = "event.second", Kind = FlowEventKind.Audit }.ToMessage()
        };
        foreach (var message in messages)
        {
            (await runtime.PublishSystemEventAsync(message, default)).IsAccepted.ShouldBeTrue();
            (await events.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            (await activity.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        }
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);
        (await runtime.PublishSystemEventAsync(messages[0], default)).Status.ShouldBe(SystemEventPublishStatus.Completed);

        observer.Metrics.Where(value => value.Name == SystemAccepted).Select(value => value.Value).ShouldBe([1d, 1d]);
        observer.Metrics.Where(value => value.Name == SystemRejected).Select(value => value.Value).ShouldBe([1d]);
        observer.Metrics.Count.ShouldBe(3);
        observer.SourceEnabledCalls.ShouldBe(2);
        observer.SourceValues.Count.ShouldBe(failure == "source.enabled" ? 0 : 2);
        if (failure != "source.enabled")
        {
            var observed = observer.SourceValues.ToArray();
            observed[0].Value.ShouldBeSameAs(messages[0]);
            observed[1].Value.ShouldBeSameAs(messages[1]);
            foreach (var value in observed) value.Key.ShouldBe(ApplicationRuntimeInstrumentation.SystemEventName);
        }
        observer.Logger.Entries.ShouldBeEmpty();
        events.Count.ShouldBe(0);
        activity.Count.ShouldBe(0);
        runtime.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(FlowEventLevel.Trace, LogLevel.Trace, true)]
    [InlineData(FlowEventLevel.Information, LogLevel.Information, true)]
    [InlineData(FlowEventLevel.Warning, LogLevel.Warning, true)]
    [InlineData(FlowEventLevel.Error, LogLevel.Error, true)]
    [InlineData(FlowEventLevel.Trace, LogLevel.Trace, false)]
    [InlineData(FlowEventLevel.Information, LogLevel.Information, false)]
    [InlineData(FlowEventLevel.Warning, LogLevel.Warning, false)]
    [InlineData(FlowEventLevel.Error, LogLevel.Error, false)]
    [InlineData((FlowEventLevel)999, LogLevel.None, true)]
    public async Task Logger_respects_level_filter_without_suppressing_events_or_metrics(
        FlowEventLevel level, LogLevel expectedLevel, bool enabled)
    {
        using var observer = new ObserverProbe(loggingEnabled: enabled);
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = runtime.Diagnostics.LinkTo(sink);
        var message = new FlowEvent { Name = "filter.level", Level = level }.ToMessage();

        runtime.TryPublishDiagnostic(message).ShouldBeTrue();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        observer.SourceValues.ShouldHaveSingleItem().Value.ShouldBeSameAs(message);
        var metric = observer.Metrics.ShouldHaveSingleItem();
        metric.Name.ShouldBe(Accepted);
        metric.Value.ShouldBe(1);
        metric.Tags.Single(pair => pair.Key == "flow.diagnostic.level").Value
            .ShouldBe(expectedLevel == LogLevel.None ? "Unknown" : level.ToString());
        observer.Logger.EnabledCalls.ShouldBe(expectedLevel == LogLevel.None ? 0 : 1);
        if (enabled && expectedLevel != LogLevel.None)
        {
            var log = observer.Logger.Entries.ShouldHaveSingleItem();
            log.Level.ShouldBe(expectedLevel);
            log.Event.Name.ShouldBe("filter.level");
            log.Text.ShouldBe("filter.level: filter.level");
        }
        else observer.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_diagnostic_source_does_not_suppress_other_channels()
    {
        using var observer = new ObserverProbe(sourceEnabled: false);
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var message = Diagnostic(0);

        runtime.TryPublishDiagnostic(message).ShouldBeTrue();
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        observer.SourceEnabledCalls.ShouldBe(1);
        observer.SourceValues.ShouldBeEmpty();
        observer.Logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
        observer.Metrics.Select(value => value.Name).ShouldBe([Accepted, Duration]);
        observer.Metrics.Select(value => value.Value).ShouldBe([1d, 0.5]);
        runtime.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Metric_instruments_can_be_enabled_independently(bool counterEnabled, bool durationEnabled)
    {
        using var observer = new ObserverProbe(counterEnabled: counterEnabled, durationEnabled: durationEnabled);
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var message = Diagnostic(0);

        runtime.TryPublishDiagnostic(message).ShouldBeTrue();
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        observer.Metrics.Where(value => value.Name == Accepted).Select(value => value.Value)
            .ShouldBe(counterEnabled ? new[] { 1d } : Array.Empty<double>());
        observer.Metrics.Where(value => value.Name == Duration).Select(value => value.Value)
            .ShouldBe(durationEnabled ? new[] { 0.5 } : Array.Empty<double>());
        observer.Metrics.Count.ShouldBe((counterEnabled ? 1 : 0) + (durationEnabled ? 1 : 0));
        foreach (var metric in observer.Metrics)
        {
            metric.Tags.Select(pair => pair.Key).ShouldBe(["flow.diagnostic.kind", "flow.diagnostic.level"]);
            metric.Tags.Select(pair => pair.Value).ShouldBe(new object?[] { "Diagnostic", "Warning" });
        }
        observer.SourceValues.ShouldHaveSingleItem().Value.ShouldBeSameAs(message);
        observer.Logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Event_validation_precedes_observation_and_terminal_state(bool observersEnabled, bool stopped)
    {
        using var observer = observersEnabled ? new ObserverProbe() : null;
        await using var runtime = new ApplicationRuntimeSignals(observer?.Logger);
        var observedEvents = new ConcurrentQueue<FlowMessage>();
        runtime.EventObserver = observedEvents.Enqueue;
        var diagnostics = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var events = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var activity = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var diagnosticLink = runtime.Diagnostics.LinkTo(diagnostics);
        using var eventLink = runtime.SystemEvents.LinkTo(events);
        using var activityLink = runtime.Activity.LinkTo(activity);
        if (stopped) runtime.Complete();

        Should.Throw<ArgumentNullException>(() => runtime.TryPublishDiagnostic((FlowMessage)null!)).ParamName.ShouldBe("message");
        (await Should.ThrowAsync<ArgumentNullException>(async () =>
            await runtime.PublishSystemEventAsync(null!, default))).ParamName.ShouldBe("message");
        var invalid = FlowMessage.Create(FlowValue.From(42));
        Should.Throw<InvalidOperationException>(() => runtime.TryPublishDiagnostic(invalid))
            .Message.ShouldBe("The flow message does not contain a canonical event value.");
        (await Should.ThrowAsync<InvalidOperationException>(async () =>
            await runtime.PublishSystemEventAsync(invalid, default)))
            .Message.ShouldBe("The flow message does not contain a canonical event value.");

        observedEvents.ShouldBeEmpty();
        if (observer is not null)
        {
            observer.Metrics.ShouldBeEmpty();
            observer.SourceEnabledCalls.ShouldBe(0);
            observer.SourceValues.ShouldBeEmpty();
            observer.Logger.EnabledCalls.ShouldBe(0);
            observer.Logger.Entries.ShouldBeEmpty();
        }
        else
        {
            ApplicationRuntimeInstrumentation.DiagnosticsAccepted.Enabled.ShouldBeFalse();
            ApplicationRuntimeInstrumentation.RequestDuration.Enabled.ShouldBeFalse();
        }
        if (!stopped)
        {
            var valid = Diagnostic(0);
            runtime.TryPublishDiagnostic(valid).ShouldBeTrue();
            (await diagnostics.ReceiveAsync(Deadline)).ShouldBeSameAs(valid);
            (await activity.ReceiveAsync(Deadline)).ShouldBeSameAs(valid);
            observedEvents.ShouldHaveSingleItem().ShouldBeSameAs(valid);
        }
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);
        diagnostics.Count.ShouldBe(0);
        events.Count.ShouldBe(0);
        activity.Count.ShouldBe(0);
        runtime.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_system_event_does_not_reach_observers_or_streams(bool stopped)
    {
        using var observer = new ObserverProbe();
        await using var runtime = new ApplicationRuntimeSignals(observer.Logger);
        var observedEvents = new ConcurrentQueue<FlowMessage>();
        runtime.EventObserver = observedEvents.Enqueue;
        var events = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var activity = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var eventLink = runtime.SystemEvents.LinkTo(events);
        using var activityLink = runtime.Activity.LinkTo(activity);
        if (stopped) runtime.Complete();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.PublishSystemEventAsync(Diagnostic(0), cancellation.Token));
        error.CancellationToken.ShouldBe(cancellation.Token);
        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);

        events.Count.ShouldBe(0);
        activity.Count.ShouldBe(0);
        observedEvents.ShouldBeEmpty();
        observer.Metrics.ShouldBeEmpty();
        observer.SourceEnabledCalls.ShouldBe(0);
        observer.SourceValues.ShouldBeEmpty();
        observer.Logger.EnabledCalls.ShouldBe(0);
        observer.Logger.Entries.ShouldBeEmpty();
        runtime.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static FlowMessage Diagnostic(int index) => new FlowEvent
    {
        Name = ApplicationDiagnosticNames.RequestCompleted,
        Level = FlowEventLevel.Warning,
        Message = $"diagnostic {index}",
        Dimensions = new Dictionary<string, object?> { ["kind"] = "Timing" },
        Measurements = new Dictionary<string, double> { ["duration.ms"] = index + 0.5 }
    }.ToMessage();

    private sealed class ProcessingNode(Func<FlowMessage, Task<FlowMessage>> process) : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message) => await EmitAsync(await process(message));
    }

    private sealed record Metric(string Name, double Value, KeyValuePair<string, object?>[] Tags, Activity? Context);
    private sealed record LogEntry(LogLevel Level, EventId Event, string Text, Activity? Context);

    private sealed class ObserverProbe : IDisposable
    {
        private readonly MeterListener _meter = new();
        private readonly IDisposable _source;
        private int _enabledCalls;
        public int SourceEnabledCalls => Volatile.Read(ref _enabledCalls);
        public ConcurrentQueue<Metric> Metrics { get; } = new();
        public ConcurrentQueue<KeyValuePair<string, object?>> SourceValues { get; } = new();
        public ConcurrentQueue<Activity?> SourceContexts { get; } = new();
        public LogProbe Logger { get; }

        public ObserverProbe(string failure = "none", bool loggingEnabled = true, bool sourceEnabled = true,
            bool counterEnabled = true, bool durationEnabled = true)
        {
            Logger = new(failure, loggingEnabled);
            _source = ApplicationRuntimeInstrumentation.DiagnosticSource.Subscribe(
                new Observer<KeyValuePair<string, object?>>(value =>
                {
                    SourceValues.Enqueue(value);
                    SourceContexts.Enqueue(Activity.Current);
                    if (failure == "source.write") throw new InvalidOperationException("source write failed");
                }), _ =>
                {
                    Interlocked.Increment(ref _enabledCalls);
                    if (failure == "source.enabled") throw new InvalidOperationException("source filter failed");
                    return sourceEnabled;
                });
            _meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name == Accepted && !counterEnabled) return;
                if (instrument.Name == Duration && !durationEnabled) return;
                if (instrument.Meter.Name == ApplicationRuntimeInstrumentation.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            void Capture(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                Metrics.Enqueue(new(instrument.Name, value, tags.ToArray(), Activity.Current));
                if ((failure == "counter" && instrument is Counter<long>) ||
                    (failure == "duration" && instrument.Name == Duration))
                    throw new InvalidOperationException("metric callback failed");
            }
            _meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Capture(instrument, value, tags));
            _meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Capture(instrument, value, tags));
            _meter.Start();
        }

        public void Dispose() { _meter.Dispose(); _source.Dispose(); }
    }

    private sealed class LogProbe(string failure, bool enabled) : ILogger
    {
        private int _enabledCalls;
        public int EnabledCalls => Volatile.Read(ref _enabledCalls);
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel)
        {
            Interlocked.Increment(ref _enabledCalls);
            if (failure == "logger.enabled") throw new InvalidOperationException("logger filter failed");
            return enabled;
        }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Enqueue(new(logLevel, eventId, formatter(state, exception), Activity.Current));
            if (failure == "logger.write") throw new InvalidOperationException("logger write failed");
        }
    }

    private sealed class Observer<T>(Action<T> receive) : IObserver<T>
    {
        public void OnNext(T value) => receive(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private sealed class TraceProbe : IDisposable
    {
        private readonly ActivityListener _listener;
        public ConcurrentQueue<Activity> Started { get; } = new();
        public ConcurrentQueue<Activity> Stopped { get; } = new();
        public TraceProbe()
        {
            _listener = new()
            {
                ShouldListenTo = source => source.Name is "FluxFlow.Engine.Runtime" or "FluxFlow.Core",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = Started.Enqueue,
                ActivityStopped = Stopped.Enqueue
            };
            ActivitySource.AddActivityListener(_listener);
        }
        public void Dispose() => _listener.Dispose();
    }
}
