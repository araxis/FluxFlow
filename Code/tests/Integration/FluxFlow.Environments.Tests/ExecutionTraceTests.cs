using System.Diagnostics;
using FluxFlow.Execution;
using FluxFlow.Environments.Telemetry;
using OpenTelemetry.Proto.Collector.Trace.V1;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class ExecutionTraceTests
{
    [Fact]
    public void Collector_preserves_standard_fields_and_log_correlation_through_transport()
    {
        using var collector = new ExecutionTraceCollector();
        using var source = new ActivitySource("FluxFlow.Core", "test-version");
        var runId = Guid.NewGuid();
        var observations = new RunObservations(runId);
        using var run = collector.Capture(observations.Traces);
        using var factory = ExecutionLogTests.CreateFactory();
        var logger = factory.CreateLogger("Original.Component");
        using var logs = logger.BeginScope(observations.Logs);
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None, "vendor=original", true);
        var values = new[] { 1, 2 };
        var eventTime = DateTimeOffset.UtcNow;
        using var activity = source.StartActivity("flow.process", ActivityKind.Consumer, parent,
            links: [new ActivityLink(parent, new ActivityTagsCollection { ["link.value"] = "original" })])!;
        activity.ShouldNotBeNull();
        activity.SetTag("flow.component.address", "main.mapper");
        activity.SetTag("values", values);
        activity.AddTag("repeated", "first");
        activity.AddTag("repeated", "last");
        activity.AddEvent(new("exception", eventTime, new ActivityTagsCollection { ["exception.stacktrace"] = "complete\n<detail>" }));
        activity.SetStatus(ActivityStatusCode.Error, "original error");
        logger.LogWarning("Original {Count}", 42);
        activity.Stop();
        values[0] = 99;

        var export = observations.Traces.Export();
        var otlp = ExportTraceServiceRequest.Parser.ParseFrom(export.Content);
        otlp.ResourceSpans.Single().ScopeSpans.Single().Spans.Single().Name.ShouldBe("flow.process");
        var span = OtlpExecutionView.Traces(export.Content, export.Dropped).Records.Single();
        span.Name.ShouldBe("flow.process");
        span.Source.ShouldBe("FluxFlow.Core");
        span.SourceVersion.ShouldBe("test-version");
        span.Kind.ShouldBe("Consumer");
        span.TraceId.ShouldBe(parent.TraceId.ToHexString());
        span.ParentSpanId.ShouldBe(parent.SpanId.ToHexString());
        span.TraceState.ShouldBe("vendor=original");
        span.TraceFlags.ShouldBe(1);
        span.StartTime.UtcDateTime.ShouldBe(activity.StartTimeUtc);
        span.Duration.ShouldBe(activity.Duration);
        span.Status.ShouldBe("Error");
        span.StatusDescription.ShouldBe("original error");
        span.Attributes["values"][0].GetInt32().ShouldBe(1);
        span.Attributes["repeated"].GetString().ShouldBe("last");
        span.Events.Single().Timestamp.ShouldBe(eventTime);
        span.Events.Single().Attributes["exception.stacktrace"].GetString().ShouldBe("complete\n<detail>");
        span.Links.Single().SpanId.ShouldBe(parent.SpanId.ToHexString());
        span.Links.Single().IsRemote.ShouldBeTrue();
        span.Links.Single().Attributes["link.value"].GetString().ShouldBe("original");
        span.Resource["service.name"].GetString().ShouldBe("FluxFlow.Execution");
        span.Attributes["flow.run.id"].GetString().ShouldBe(runId.ToString());
        var logExport = observations.Logs.Export();
        var log = OtlpExecutionView.Logs(logExport.Content, logExport.Dropped).Records.Single();
        log.Message.ShouldBe("Original 42");
        log.Level.ShouldBe("Warning");
        log.Telemetry!.TraceId.ShouldBe(span.TraceId);
        log.Telemetry.SpanId.ShouldBe(span.SpanId);
        log.Telemetry.Attributes.Single(item => item.Name == "flow.run.id").Value.GetString().ShouldBe(runId.ToString());
    }

    [Fact]
    public async Task Concurrent_runs_route_spans_to_the_starting_run_and_reject_late_completions()
    {
        using var collector = new ExecutionTraceCollector();
        using var source = new ActivitySource("FluxFlow.Core");
        using (source.StartActivity("outside")) { }
        var first = new ExecutionTraceBuffer();
        var second = new ExecutionTraceBuffer();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker;
        Activity crossing;
        using (collector.Capture(first))
        {
            crossing = source.StartActivity("crossing")!;
            worker = Task.Run(async () => { await release.Task; using (source.StartActivity("first.worker")) { } });
        }
        using (collector.Capture(second))
        {
            using (source.StartActivity("second")) { }
            crossing.Dispose();
            release.SetResult();
            await worker;
        }
        first.Snapshot().Records.Select(item => item.Name).Order().ShouldBe(["crossing", "first.worker"]);
        second.Snapshot().Records.Single().Name.ShouldBe("second");
        using (collector.Capture(first))
        {
            using var late = source.StartActivity("late");
            first.Complete();
        }
        first.Snapshot().Records.Count.ShouldBe(2);
        second.Snapshot().Records.Count.ShouldBe(1);
    }

    [Fact]
    public void Collectors_do_not_duplicate_each_others_records_and_buffers_are_bounded()
    {
        using var firstCollector = new ExecutionTraceCollector();
        using var secondCollector = new ExecutionTraceCollector();
        using var source = new ActivitySource("FluxFlow.Core");
        var first = new ExecutionTraceBuffer();
        var second = new ExecutionTraceBuffer();
        ExecutionTraceBuffer.Capacity.ShouldBeGreaterThan(ExecutionLogBuffer.Capacity);
        using (firstCollector.Capture(first))
            for (var i = 0; i < ExecutionTraceBuffer.Capacity + 3; i++) using (source.StartActivity($"first.{i}")) { }
        using (secondCollector.Capture(second)) using (source.StartActivity("second")) { }
        first.Snapshot().Records.Count.ShouldBe(ExecutionTraceBuffer.Capacity);
        first.Snapshot().Records[0].Sequence.ShouldBe(4);
        first.Snapshot().Dropped.ShouldBe(3);
        second.Snapshot().Records.Single().Name.ShouldBe("second");
        var retained = first.Export();
        first.Snapshot().Records[0].Name.ShouldBe("first.3");
        first.Snapshot().Records[^1].Name.ShouldBe($"first.{ExecutionTraceBuffer.Capacity + 2}");
        first.Complete();
        using (firstCollector.Capture(first)) using (source.StartActivity("late")) { }
        first.Export().Content.ShouldBe(retained.Content);
        first.Export().Dropped.ShouldBe(retained.Dropped);
    }
}
