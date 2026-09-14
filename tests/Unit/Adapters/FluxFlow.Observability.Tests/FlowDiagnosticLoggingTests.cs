using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Observability.Tests;

[Collection("Process diagnostics")]
public sealed class FlowDiagnosticLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logs_original_exception_and_identifiers_with_optional_trace(bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var factory = new RecordingFactory();
        using var subscription = FlowDiagnosticLogging.Subscribe(factory);
        var exception = new InvalidOperationException("failure", new ArgumentException("inner"));
        await using var node = new Probe(exception);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Input.SendAsync(input);
        var failure = await node.Output.ReceiveAsync(TimeSpan.FromSeconds(5));
        failure.Error!.Code.ShouldBe("node.processing_failed");
        var entry = factory.Entries.ShouldHaveSingleItem();
        factory.Category.ShouldBe(FlowDiagnosticLogging.LoggerCategory);
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Event.ShouldBe(new EventId(1, "ProcessingException"));
        entry.Exception.ShouldBeSameAs(exception);
        entry.Exception!.StackTrace.ShouldNotBeNullOrWhiteSpace();
        entry.Exception.InnerException.ShouldBeSameAs(exception.InnerException);
        entry.Fields["ComponentType"].ShouldBe(typeof(Probe).FullName);
        entry.Fields["MessageId"].ShouldBe(input.MessageId.ToString());
        entry.Fields["CorrelationId"].ShouldBe(input.CorrelationId.ToString());
        entry.Fields["BusinessTraceId"].ShouldBe(input.TraceId.ToString());
        (entry.Activity is not null).ShouldBe(tracing);
        if (tracing) entry.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        subscription.Dispose();
        var received1 = node.Output.ReceiveAsync(TimeSpan.FromSeconds(5));
        await node.Input.SendAsync(input);
        (await received1).IsError.ShouldBeTrue();
        factory.Entries.Count.ShouldBe(1);
        factory.Disposed.ShouldBeFalse();
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_throwing_logger_does_not_change_processing(bool throws)
    {
        using var factory = new RecordingFactory { Enabled = throws, Throws = throws };
        using var subscription = FlowDiagnosticLogging.Subscribe(factory);
        await using var node = new Probe(new InvalidOperationException("failure"));
        var received2 = node.Output.ReceiveAsync(TimeSpan.FromSeconds(5));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await received2).IsError.ShouldBeTrue();
        var received3 = node.Output.ReceiveAsync(TimeSpan.FromSeconds(5));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await received3).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        factory.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Discovers_existing_and_future_listeners_and_unsubscribes(bool existing)
    {
        using var factory = new RecordingFactory();
        DiagnosticListener? source = existing ? new(FlowDiagnostics.ListenerName) : null;
        using var subscription = FlowDiagnosticLogging.Subscribe(factory);
        source ??= new(FlowDiagnostics.ListenerName);
        using (source)
        {
            var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            var failure = new FlowProcessingException(new Exception("failure"), "probe",
                message.MessageId, null, message.TraceId, null);
            source.IsEnabled("unrelated").ShouldBeFalse();
            source.Write("unrelated", failure);
            source.Write(FlowDiagnostics.ProcessingExceptionEvent, new object());
            Parallel.For(0, 20, _ => source.Write(FlowDiagnostics.ProcessingExceptionEvent, failure));
            factory.Entries.Count.ShouldBe(20);
            subscription.Dispose();
            subscription.Dispose();
            source.IsEnabled(FlowDiagnostics.ProcessingExceptionEvent).ShouldBeFalse();
            source.Write(FlowDiagnostics.ProcessingExceptionEvent, failure);
            using var later = new DiagnosticListener(FlowDiagnostics.ListenerName);
            later.IsEnabled(FlowDiagnostics.ProcessingExceptionEvent).ShouldBeFalse();
            factory.Entries.Count.ShouldBe(20);
            factory.Disposed.ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("disabled")]
    [InlineData("throwing")]
    public async Task Source_fault_logs_once_without_message_or_ambient_trace(string mode)
    {
        using var ambient = new Activity("host").Start();
        using var factory = new RecordingFactory { Enabled = mode != "disabled", Throws = mode == "throwing" };
        using var subscription = FlowDiagnosticLogging.Subscribe(factory);
        var exception = new InvalidOperationException("production failed", new ArgumentException("inner"));
        await using var source = new SourceProbe(exception);
        await source.StartAsync();
        var actual = await Should.ThrowAsync<InvalidOperationException>(
            () => source.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        actual.ShouldBeSameAs(exception);
        source.Fault(exception);
        source.Fault(new Exception("later"));
        if (mode == "enabled")
        {
            var entry = factory.Entries.ShouldHaveSingleItem();
            entry.Event.ShouldBe(new EventId(2, "LifecycleException"));
            entry.Exception.ShouldBeSameAs(exception);
            exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
            entry.Fields["ComponentType"].ShouldBe(typeof(SourceProbe).FullName);
            entry.Fields["Operation"].ShouldBe("source");
            entry.Fields.ContainsKey("MessageId").ShouldBeFalse();
            entry.Fields.ContainsKey("BusinessTraceId").ShouldBeFalse();
            entry.Activity.ShouldBeNull();
        }
        else factory.Entries.ShouldBeEmpty();
        ambient.Status.ShouldBe(ActivityStatusCode.Unset);
        Activity.Current.ShouldBeSameAs(ambient);
        subscription.Dispose();
        FlowDiagnostics.LifecycleException(source, "source", exception);
        factory.Entries.Count.ShouldBe(mode == "enabled" ? 1 : 0);
        factory.Disposed.ShouldBeFalse();
    }

    private sealed class SourceProbe(Exception exception) : FlowSource
    {
        protected override Task RunAsync(CancellationToken cancellationToken) => throw exception;
    }

    private sealed class Probe(Exception exception) : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message)
        {
            if (message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == 1) throw exception;
            return Task.FromResult(message.With(message.Value));
        }
    }

    private sealed record Entry(LogLevel Level, EventId Event, Exception? Exception,
        Dictionary<string, object?> Fields, Activity? Activity);

    private sealed class RecordingFactory : ILoggerFactory, ILogger
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();
        public bool Enabled { get; init; } = true;
        public bool Throws { get; init; }
        public bool Disposed { get; private set; }
        public string? Category { get; private set; }
        public ILogger CreateLogger(string categoryName) { Category = categoryName; return this; }
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (Throws) throw new InvalidOperationException("provider failed");
            Entries.Enqueue(new(logLevel, eventId, exception,
                ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(x => x.Key, x => x.Value),
                Activity.Current));
        }
        public void Dispose() => Disposed = true;
    }
}
