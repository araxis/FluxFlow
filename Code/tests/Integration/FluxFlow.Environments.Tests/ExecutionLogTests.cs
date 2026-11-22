using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FluxFlow.Execution;
using FluxFlow.Environments.Telemetry;
using OpenTelemetry.Proto.Collector.Logs.V1;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class ExecutionLogTests
{
    [Fact]
    public void Nested_runner_and_engine_scopes_have_one_unambiguous_run_identity()
    {
        using var logging = CreateFactory();
        var logger = logging.CreateLogger("Original.Dependency");
        var id = Guid.NewGuid();
        var observations = new RunObservations(id);
        using (logger.BeginScope(observations.Diagnostics))
        using (logger.BeginScope(observations.Diagnostics))
        using (logger.BeginScope(observations.Logs))
            logger.LogWarning("Original {Value}", 42);
        var record = observations.Logs.Snapshot().Records.Single();
        record.Message.ShouldBe("Original 42");
        record.Level.ShouldBe("Warning");
        record.Telemetry!.Attributes.Where(item => item.Name == "flow.run.id")
            .ShouldHaveSingleItem().Value.GetString().ShouldBe(id.ToString());
        record.Telemetry.Attributes.Where(item => item.Name == "flow.telemetry.origin")
            .ShouldHaveSingleItem().Value.GetString().ShouldBe("engine");
        observations.Diagnostics.Snapshot().Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Output_capture_stops_during_continuous_production_without_consuming_delivery_or_accepting_late_records()
    {
        var node = new OutputNode();
        var address = ComponentAddress.Parse("main.source");
        var services = new ServiceCollection();
        services.AddComponentFactory(address, _ => node).AddOutputPort<OutputNode>(address, "Output", source => source.Output);
        await using var provider = services.BuildServiceProvider();
        var observations = new RunObservations();
        await using var capture = new WorkflowOutputCapture(observations, NullLogger.Instance);
        capture.Attach(provider);
        long delivered = 0;
        var business = new ActionBlock<FlowMessage>(_ => Interlocked.Increment(ref delivered));
        using var link = node.Output.LinkTo(business, new DataflowLinkOptions { PropagateCompletion = true });
        using var stopProducer = new CancellationTokenSource();
        var message = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(42)));
        var producer = Task.Run(async () =>
        {
            while (!stopProducer.IsCancellationRequested)
                await node.Output.SendAsync(message, stopProducer.Token);
        });
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (observations.Outputs.Snapshot().Records.Count == 0) await Task.Delay(10, deadline.Token);
            await capture.DisposeAsync().AsTask().WaitAsync(deadline.Token);
            observations.Complete();
            var retained = observations.Outputs.Snapshot();
            retained.Records.ShouldNotBeEmpty();
            retained.Records.ShouldAllBe(record => record.Error == null);
            var before = Interlocked.Read(ref delivered);
            while (Interlocked.Read(ref delivered) == before) await Task.Delay(10, deadline.Token);
            observations.Outputs.Snapshot().Records.ShouldBe(retained.Records);
            observations.Outputs.Snapshot().Dropped.ShouldBe(retained.Dropped);
            producer.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            stopProducer.Cancel();
            try { await producer; } catch (OperationCanceledException) { }
            node.Complete();
            await business.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class OutputNode : IFlowNode
    {
        public BufferBlock<FlowMessage> Output { get; } = new(new DataflowBlockOptions { BoundedCapacity = 256 });
        public Task Completion => Output.Completion;
        public void Complete() => Output.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)Output).Fault(exception);
        public ValueTask DisposeAsync() { Complete(); return new(Completion); }
    }
    internal static ILoggerFactory CreateFactory(BaseProcessor<LogRecord>? observer = null, LogLevel minimum = LogLevel.Information) => LoggerFactory.Create(builder => builder.SetMinimumLevel(minimum)
        .AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("execution-tests"));
            if (observer is not null) options.AddProcessor(observer);
            options.AddProcessor(new ExecutionLogProcessor());
        }));

    [Theory]
    [InlineData(LogLevel.Trace, 1)]
    [InlineData(LogLevel.Debug, 5)]
    [InlineData(LogLevel.Information, 9)]
    [InlineData(LogLevel.Warning, 13)]
    [InlineData(LogLevel.Error, 17)]
    [InlineData(LogLevel.Critical, 21)]
    public void Sdk_capture_preserves_correlation_attributes_scopes_and_original_records(LogLevel level, int severity)
    {
        var observer = new TimestampObserver();
        using var factory = CreateFactory(observer, LogLevel.Trace);
        var logger = factory.CreateLogger("Dependency.MixedCase");
        var buffer = new ExecutionLogBuffer();
        using var activity = new Activity("processing").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        using var run = logger.BeginScope(buffer);
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["connection.name"] = "broker" });
        var numbers = new[] { 1, 2 };
        logger.Log(level, new EventId(42, "OriginalEvent"), new InvalidOperationException("Full <exception>"),
            "Received {Count} values: {Values}", 2, numbers);
        numbers[0] = 99;
        var original = buffer.Snapshot().Records.Single();
        var status = new EnvironmentRunStatus(Guid.NewGuid(), "Stopped", [], Logs: buffer.Snapshot());
        var statusJson = JsonSerializer.Serialize(status, EnvironmentJsonContext.Default.EnvironmentRunStatus);
        statusJson.ShouldNotContain("\"Logs\"");
        var export = buffer.Export();
        var otlp = ExportLogsServiceRequest.Parser.ParseFrom(export.Content);
        otlp.ResourceLogs.Single().ScopeLogs.Single().LogRecords.Single().EventName.ShouldBe("OriginalEvent");
        var record = OtlpExecutionView.Logs(export.Content, export.Dropped).Records.Single();
        record.Timestamp.UtcDateTime.ShouldBe(observer.Timestamp);
        record.Message.ShouldBe(original.Message);
        record.Level.ShouldBe(level.ToString());
        record.Category.ShouldBe("Dependency.MixedCase");
        record.EventId.ShouldBe(42);
        record.EventName.ShouldBe("OriginalEvent");
        record.Exception.ShouldBe(original.Exception);
        var telemetry = record.Telemetry!;
        telemetry.SeverityNumber.ShouldBe(severity);
        telemetry.SeverityText.ShouldBe(level.ToString());
        telemetry.TraceId.ShouldBe(activity.TraceId.ToHexString());
        telemetry.SpanId.ShouldBe(activity.SpanId.ToHexString());
        telemetry.TraceFlags.ShouldBe(1);
        telemetry.Body.ShouldBe(original.Message);
        telemetry.Attributes.Single(item => item.Name == "{OriginalFormat}").Value.GetString().ShouldBe("Received {Count} values: {Values}");
        telemetry.ObservedTimestamp.ShouldBeGreaterThanOrEqualTo(record.Timestamp);
        telemetry.Attributes.Single(item => item.Name == "Count").Value.GetInt32().ShouldBe(2);
        telemetry.Attributes.Single(item => item.Name == "Values").Value[0].GetInt32().ShouldBe(1);
        telemetry.Attributes.Single(item => item.Name == "connection.name").Value.GetString().ShouldBe("broker");
        telemetry.Resource.Single(item => item.Name == "service.name").Value.GetString().ShouldBe("execution-tests");
    }

    private sealed class TimestampObserver : BaseProcessor<LogRecord>
    {
        public DateTime Timestamp { get; private set; }
        public override void OnEnd(LogRecord record) => Timestamp = record.Timestamp;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void Sdk_log_details_survive_conflicting_scope_attributes(int eventId)
    {
        using var factory = CreateFactory();
        var logger = factory.CreateLogger("Original.Dependency");
        var buffer = new ExecutionLogBuffer();
        using var run = logger.BeginScope(buffer);
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["logrecord.event.id"] = long.MaxValue,
            ["exception.stacktrace"] = "Scope metadata, not the logged exception"
        });
        var exception = new InvalidOperationException("Original outer", new IOException("Complete inner detail"));
        logger.LogError(new EventId(eventId, "OriginalEvent"), exception, "Original message");
        var export = buffer.Export();
        var record = OtlpExecutionView.Logs(export.Content, export.Dropped).Records.Single();
        record.EventId.ShouldBe(eventId);
        record.EventName.ShouldBe("OriginalEvent");
        record.Exception.ShouldBe(exception.ToString());
        record.Message.ShouldBe("Original message");
        // Keep both attributes in the original payload, even when their names collide.
        record.Telemetry!.Attributes.Count(item => item.Name == "logrecord.event.id").ShouldBe(eventId == 0 ? 1 : 2);
        record.Telemetry.Attributes.Last(item => item.Name == "logrecord.event.id").Value.GetInt64().ShouldBe(long.MaxValue);
        record.Telemetry.Attributes.Count(item => item.Name == "exception.stacktrace").ShouldBe(2);
    }
    [Fact]
    public void Output_and_runner_buffers_are_bounded_serializable_and_reject_late_records()
    {
        var observations = new RunObservations();
        using var factory = CreateFactory();
        var logger = factory.CreateLogger("Runner");
        using var scope = logger.BeginScope(observations.Diagnostics);
        using (var document = JsonDocument.Parse("""{"Value":{"Reading":42},"Headers":{"CaseSensitive":"yes"},"MessageId":"original"}"""))
            for (var i = 0; i < ExecutionOutputBuffer.Capacity + 2; i++)
                observations.Outputs.Append("main.mapper.Output", document.RootElement);
        logger.LogInformation("Workflow execution started.");
        observations.Outputs.AddDropped(3);
        observations.Complete();
        observations.Outputs.Append("late", null, "late failure");
        logger.LogInformation("Late lifecycle record.");
        var status = new EnvironmentRunStatus(Guid.NewGuid(), "Stopped", [], Diagnostics: observations.Diagnostics.Snapshot(), Outputs: observations.Outputs.Snapshot());
        var transported = JsonSerializer.Deserialize(JsonSerializer.Serialize(status, EnvironmentJsonContext.Default.EnvironmentRunStatus), EnvironmentJsonContext.Default.EnvironmentRunStatus)!;
        transported.Logs.ShouldBeNull();
        transported.Diagnostics.ShouldBeNull();
        var export = observations.Diagnostics.Export();
        OtlpExecutionView.Logs(export.Content, export.Dropped).Records.Single().Message.ShouldBe("Workflow execution started.");
        transported.Outputs!.Dropped.ShouldBe(5);
        transported.Outputs.Records.Count.ShouldBe(ExecutionOutputBuffer.Capacity);
        transported.Outputs.Records[0].Sequence.ShouldBe(3);
        transported.Outputs.Records[0].Message!.Value.GetProperty("Headers").GetProperty("CaseSensitive").GetString().ShouldBe("yes");
        transported.Outputs.Records[0].Message!.Value.GetProperty("MessageId").GetString().ShouldBe("original");
    }

    [Fact]
    public async Task Standard_logging_preserves_details_filters_and_async_run_isolation()
    {
        using var factory = CreateFactory();
        var logger = factory.CreateLogger("Dependency.OriginalCategory");
        var first = new ExecutionLogBuffer();
        var second = new ExecutionLogBuffer();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = Should.Throw<InvalidOperationException>(() =>
            throw new InvalidOperationException("outer <detail>", new IOException("inner\ncomplete detail")));
        Task background;
        var before = DateTimeOffset.UtcNow;
        using (logger.BeginScope(first))
        {
            logger.LogDebug("Filtered by configured level");
            logger.LogWarning(new EventId(42, "OriginalEvent"), error, "Original {Value}\nnext line", "MiXeD");
            background = Task.Run(async () => { await release.Task; logger.LogError("Background from first run"); });
        }
        using (logger.BeginScope(second)) logger.LogInformation("Second run only");
        logger.LogError("Outside any run");
        release.SetResult();
        await background;
        var records = first.Snapshot().Records;
        records.Count.ShouldBe(2);
        var record = records[0];
        record.Timestamp.ShouldBeInRange(before, DateTimeOffset.UtcNow);
        record.Level.ShouldBe("Warning");
        record.Category.ShouldBe("Dependency.OriginalCategory");
        record.EventId.ShouldBe(42);
        record.EventName.ShouldBe("OriginalEvent");
        record.Message.ShouldBe("Original MiXeD\nnext line");
        record.Exception.ShouldBe(error.ToString());
        records[1].Message.ShouldBe("Background from first run");
        second.Snapshot().Records.Single().Message.ShouldBe("Second run only");
        first.Complete();
        using (logger.BeginScope(first)) logger.LogCritical("Late after Stop");
        JsonSerializer.Serialize(first.Snapshot().Records).ShouldBe(JsonSerializer.Serialize(records));
    }

    [Fact]
    public void Bounded_snapshot_and_status_transport_preserve_original_records()
    {
        var buffer = new ExecutionLogBuffer();
        using var factory = CreateFactory();
        var logger = factory.CreateLogger("Dependency");
        using var scope = logger.BeginScope(buffer);
        var exception = new InvalidOperationException("Full\nexception <detail>");
        for (var i = 0; i < ExecutionLogBuffer.Capacity + 3; i++)
            logger.LogCritical(new EventId(17, "Event"), exception, "record {Index}", i);
        var snapshot = buffer.Snapshot();
        snapshot.Dropped.ShouldBe(3);
        snapshot.Records.Count.ShouldBe(ExecutionLogBuffer.Capacity);
        snapshot.Records[0].Sequence.ShouldBe(4);
        var export = buffer.Export();
        var roundtrip = OtlpExecutionView.Logs(export.Content, export.Dropped);
        JsonSerializer.Serialize(roundtrip).ShouldBe(JsonSerializer.Serialize(snapshot));
        roundtrip.Dropped.ShouldBe(3);
        roundtrip.Records[0].Exception.ShouldBe(exception.ToString());
    }
}
