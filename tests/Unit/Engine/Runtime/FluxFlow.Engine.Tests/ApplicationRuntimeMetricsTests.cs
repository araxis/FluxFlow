using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RuntimeMetricsCollection
{
    public const string Name = "Runtime metrics listeners";
}

[Collection(RuntimeMetricsCollection.Name)]
public sealed class ApplicationRuntimeMetricsTests
{
    private const string Accepted = "fluxflow.runtime.diagnostics.accepted";
    private const string RequestDuration = "fluxflow.runtime.request.duration";

    [Fact]
    public void Diagnostic_metric_labels_are_bounded_across_distinct_events()
    {
        using var metrics = new Measurements();

        for (var index = 0; index < 512; index++)
        {
            var message = CustomMessage(index);
            ApplicationRuntimeInstrumentation.RecordDiagnostic(message, message.ToFlowEvent(), logger: null);
        }

        var accepted = metrics.Values.Where(value => value.Name == Accepted).ToArray();
        accepted.Length.ShouldBe(512);
        foreach (var measurement in accepted)
        {
            measurement.Value.ShouldBe(1);
            measurement.Tags.ShouldBe(new KeyValuePair<string, object?>[]
            {
                new("flow.diagnostic.kind", "Diagnostic"),
                new("flow.diagnostic.level", "Information")
            });
        }
        metrics.Values.Where(value => value.Name != Accepted).ShouldBeEmpty();
    }

    public static IEnumerable<object[]> Categories()
    {
        foreach (var kind in Enum.GetValues<FlowEventKind>())
            foreach (var level in Enum.GetValues<FlowEventLevel>())
                yield return [(int)kind, (int)level, kind.ToString(), level.ToString()];

        yield return [-1, (int)FlowEventLevel.Warning, "Unknown", "Warning"];
        yield return [(int)FlowEventKind.Audit, 1001, "Audit", "Unknown"];
        yield return [1001, -1, "Unknown", "Unknown"];
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void Diagnostic_metric_categories_use_only_defined_enum_values(
        int kind, int level, string expectedKind, string expectedLevel)
    {
        using var metrics = new Measurements();
        var message = new FlowEvent
        {
            Name = "custom.category",
            Kind = (FlowEventKind)kind,
            Level = (FlowEventLevel)level,
            Dimensions = new Dictionary<string, object?> { ["kind"] = "caller override" }
        }.ToMessage();

        ApplicationRuntimeInstrumentation.RecordDiagnostic(message, message.ToFlowEvent(), logger: null);

        var measurement = metrics.Values.ShouldHaveSingleItem();
        measurement.Name.ShouldBe(Accepted);
        measurement.Value.ShouldBe(1);
        measurement.Tags.ShouldBe(new KeyValuePair<string, object?>[]
        {
            new("flow.diagnostic.kind", expectedKind),
            new("flow.diagnostic.level", expectedLevel)
        });
    }

    [Theory]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "diagnostic", "duration.ms", "0", 0d)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "diagnostic", "duration.ms", "12.5", 12.5)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "diagnostic", "duration.ms", "1.25e3", 1250d)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "diagnostic", "duration.ms", "-0.1", null)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "diagnostic", "duration.ms", "1e999", null)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "diagnostic", "duration.ms", "-1e999", null)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "diagnostic", "value", "12.5", null)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "domain", "duration.ms", "12.5", null)]
    [InlineData(ApplicationDiagnosticNames.RequestCompleted, "1001", "duration.ms", "12.5", null)]
    [InlineData("custom.timing", "diagnostic", "duration.ms", "12.5", null)]
    [InlineData(ApplicationDiagnosticNames.InputAccepted, "diagnostic", "duration.ms", "12.5", null)]
    public void Request_duration_records_only_matching_finite_nonnegative_elapsed_values(
        string name, string kind, string key, string duration, double? expected)
    {
        using var metrics = new Measurements();
        using var payload = JsonDocument.Parse($$"""
            {
              "type": "{{name}}",
              "kind": "{{kind}}",
              "level": "warning",
              "dimensions": { "subject": "customer-42", "unit": "caller-defined" },
              "measurements": { "{{key}}": {{duration}} }
            }
            """);
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(payload.RootElement));

        ApplicationRuntimeInstrumentation.RecordDiagnostic(message, message.ToFlowEvent(), logger: null);

        metrics.Values.Count(value => value.Name == Accepted).ShouldBe(1);
        var durations = metrics.Values.Where(value => value.Name == RequestDuration).ToArray();
        if (expected is null)
        {
            durations.ShouldBeEmpty();
        }
        else
        {
            var measurement = durations.ShouldHaveSingleItem();
            measurement.Value.ShouldBe(expected.Value);
            measurement.Unit.ShouldBe("ms");
            measurement.Tags.ShouldBe(new KeyValuePair<string, object?>[]
            {
                new("flow.diagnostic.kind", "Diagnostic"),
                new("flow.diagnostic.level", "Warning")
            });
        }
    }

    [Fact]
    public async Task Custom_measurements_remain_in_original_event_streams_without_automatic_histograms()
    {
        using var metrics = new Measurements();
        var observed = new ConcurrentQueue<object?>();
        using var observer = ApplicationRuntimeInstrumentation.DiagnosticSource.Subscribe(
            new Observer<KeyValuePair<string, object?>>(item => observed.Enqueue(item.Value)));
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();
        var diagnostics = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var activity = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var diagnosticLink = runtime.Diagnostics.LinkTo(diagnostics);
        using var activityLink = runtime.Activity.LinkTo(activity);
        var message = CustomMessage(42);

        runtime.TryPublishDiagnostic(message).ShouldBeTrue();

        var delivered = await diagnostics.ReceiveAsync(TimeSpan.FromSeconds(5));
        delivered.ShouldBeSameAs(message);
        (await activity.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
        observed.ShouldHaveSingleItem().ShouldBeSameAs(message);
        var data = delivered.ToFlowEvent();
        data.Name.ShouldBe("custom.event.42");
        data.Dimensions["subject"].ShouldBe("customer-42");
        data.Dimensions["unit"].ShouldBe("unit-42");
        data.Measurements["value"].ShouldBe(42.5);
        metrics.Values.ShouldHaveSingleItem().Name.ShouldBe(Accepted);
    }

    [Fact]
    public async Task Queued_request_records_duration_only_after_reply_and_matches_its_event()
    {
        using var metrics = new Measurements();
        var completedEvents = new ConcurrentQueue<FlowMessage>();
        using var observer = ApplicationRuntimeInstrumentation.DiagnosticSource.Subscribe(
            new Observer<KeyValuePair<string, object?>>(item =>
            {
                if (item.Value is FlowMessage message &&
                    message.ToFlowEvent().Name == ApplicationDiagnosticNames.RequestCompleted)
                    completedEvents.Enqueue(message);
            }));
        var input = ApplicationAddress.WorkflowPort("Main", "Processor", "Input");
        var output = ApplicationAddress.WorkflowPort("Main", "Processor", "Output");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(input).AddOutput(output).Build();
        var requests = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var replies = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        await using var inputLink = await runtime.AttachInputAsync(input, requests);
        using var outputLink = runtime.AttachOutput(output, replies);
        var original = FlowMessage.Create("request");
        var deadline = TimeSpan.FromSeconds(5);

        var pending = runtime.SendAndReceiveAsync<string, string>(input, output, original, deadline);
        var request = await requests.ReceiveAsync(deadline);
        pending.IsCompleted.ShouldBeFalse();
        metrics.Values.Where(value => value.Name == RequestDuration).ShouldBeEmpty();
        completedEvents.ShouldBeEmpty();
        replies.Post(request.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("reply", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        var result = await pending.WaitAsync(deadline);

        result.Status.ShouldBe(PortRequestStatus.Received);
        result.Response.ShouldNotBeNull().Value.ShouldBe("reply");
        var eventMessage = completedEvents.ShouldHaveSingleItem();
        eventMessage.TraceId.ShouldBe(original.TraceId);
        var data = eventMessage.ToFlowEvent();
        data.Dimensions["status"].ShouldBe("Received");
        var measurement = metrics.Values.Where(value => value.Name == RequestDuration).ShouldHaveSingleItem();
        measurement.Value.ShouldBe(data.Measurements["duration.ms"]);
        measurement.Value.ShouldBeGreaterThanOrEqualTo(0);
        measurement.Unit.ShouldBe("ms");
        measurement.Tags.ShouldBe(new KeyValuePair<string, object?>[]
        {
            new("flow.diagnostic.kind", "Diagnostic"),
            new("flow.diagnostic.level", "Trace")
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_metric_listener_does_not_fault_runtime_or_lose_event_stream_data(bool failHistogram)
    {
        using var metrics = new Measurements(throwOnInstrument: failHistogram ? RequestDuration : Accepted);
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();
        var diagnostics = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = runtime.Diagnostics.LinkTo(diagnostics);

        for (var index = 0; index < 2; index++)
        {
            var message = new FlowEvent
            {
                Name = ApplicationDiagnosticNames.RequestCompleted,
                Measurements = new Dictionary<string, double> { ["duration.ms"] = index + 0.5 }
            }.ToMessage();
            runtime.TryPublishDiagnostic(message).ShouldBeTrue();
            (await diagnostics.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
        }

        metrics.Values.Count.ShouldBe(4);
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Active);
        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Completed);
    }

    private static FlowMessage CustomMessage(int index)
        => new FlowEvent
        {
            Name = $"custom.event.{index}",
            Kind = FlowEventKind.Diagnostic,
            Level = FlowEventLevel.Information,
            Dimensions = new Dictionary<string, object?>
            {
                ["kind"] = $"kind-{index}",
                ["subject"] = $"customer-{index}",
                ["unit"] = $"unit-{index}",
                ["message_id"] = $"message-{index}"
            },
            Measurements = new Dictionary<string, double> { ["value"] = index + 0.5 }
        }.ToMessage();

    private sealed record Measurement(
        string Name, string? Unit, double Value, KeyValuePair<string, object?>[] Tags);

    private sealed class Measurements : IDisposable
    {
        private readonly MeterListener _listener = new();
        public ConcurrentQueue<Measurement> Values { get; } = new();

        public Measurements(string? throwOnInstrument = null)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApplicationRuntimeInstrumentation.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                Values.Enqueue(new(instrument.Name, instrument.Unit, value, tags.ToArray()));
                if (instrument.Name == throwOnInstrument)
                    throw new InvalidOperationException("measurement listener failed");
            });
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                Values.Enqueue(new(instrument.Name, instrument.Unit, value, tags.ToArray()));
                if (instrument.Name == throwOnInstrument)
                    throw new InvalidOperationException("measurement listener failed");
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnNext(T value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
