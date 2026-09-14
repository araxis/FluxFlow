using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

[Collection("Logger diagnostics")]
public sealed class MeasurementDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failures_preserve_original_exceptions_and_counting_rules(bool metrics, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        var original = new InvalidOperationException("selector failed", new ArgumentException("inner"));
        var engine = new RecordingExpressionEngine((_, context, _) =>
        {
            var input = (int)context.Variables["input"]!;
            if (input < 0) throw original;
            return input > 0;
        });
        await using IFlowComponent node = metrics
            ? new FlowMetricsNode<int>(new FlowMetricsOptions(), new Selector(original))
            : new FlowCounterNode<int>(new FlowCounterOptions { Predicate = "positive" }, engine);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Ports.Outputs["Output"].LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var bad = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Ports.Inputs["Input"].SendAsync(bad);
        var failure = await output.ReceiveAsync(timeout);
        failure.Error!.Code.ShouldBe(metrics ? ObservabilityErrorCodeNames.MetricsSizeSelectorFailed : ObservabilityErrorCodeNames.CounterPredicateFailed);
        failure.CausationId.ShouldBe(bad.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        observed.Exception.ShouldBeSameAs(original);
        observed.Exception.InnerException.ShouldBeSameAs(original.InnerException);
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(node.GetType().FullName);
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("invalid", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).Error!.Code.ShouldBe(metrics ? ObservabilityErrorCodeNames.MetricsFailed : ObservabilityErrorCodeNames.CounterPredicateFailed);
        observer.Values.Count.ShouldBe(2);
        observer.Values[1].Exception.ShouldBeOfType<JsonException>();
        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        if (metrics)
        {
            var snapshot = success.Value!.Json.Deserialize<FlowMetricSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            snapshot.Count.ShouldBe(2); // A size-selector failure still counts as an observation.
            snapshot.TotalSize.ShouldBe(3);
            snapshot.AverageSize.ShouldBe(3);
        }
        else success.Value!.Json.Deserialize<FlowCounterSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Count.ShouldBe(1);
        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(0, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var zero = await output.ReceiveAsync(timeout);
        zero.IsError.ShouldBeFalse();
        if (!metrics)
        {
            var snapshot = zero.Value!.Json.Deserialize<FlowCounterSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            snapshot.Count.ShouldBe(1);
            snapshot.RejectedCount.ShouldBe(1);
        }
        await node.Ports.Inputs["Input"].SendAsync(failure);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(2);
    }

    private sealed class Selector(Exception error) : IObservabilityValueSelector<int>
    {
        public object? Select(int input, ObservabilityNodeContext context)
        {
            if (input < 0) throw error;
            return input;
        }
    }

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        public Observer() => _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == FlowDiagnostics.ListenerName)
                _subscriptions.Add(value.Subscribe(this, name => name == FlowDiagnostics.ProcessingExceptionEvent));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            Values.Add((FlowProcessingException)value.Value!);
            throw new InvalidOperationException("observer failed");
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
