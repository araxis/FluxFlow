using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Metrics.Tests;

[Collection(MetricTraceCollection.Name)]
public sealed class MetricsProcessingDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Caught_exceptions_are_reported_but_group_limit_is_not_an_exception(bool tracing, bool throwingObserver)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer(throwingObserver);
        var clock = new FailingClock();
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { MaxGroups = 1 }, clock);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);

        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).Error!.Code.ShouldBe("metrics.sample.invalid_shape");
        observer.Values.ShouldHaveSingleItem().Exception.ShouldBeOfType<JsonException>();

        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new MetricSampleInput { Size = -1 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var invalid = await output.ReceiveAsync(timeout);
        invalid.Error!.Code.ShouldBe(MetricsErrorCodeNames.InvalidSample);
        observer.Values.Count.ShouldBe(2);
        observer.Values[1].Exception.Message.ShouldContain("size cannot be negative");

        var original = new InvalidOperationException("clock failed", new ArgumentException("inner"));
        clock.NextFailure = original;
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new MetricSampleInput { Value = 2, Group = "a" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            correlationId: CorrelationId.New());
        await node.Input.SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.Error!.Code.ShouldBe(MetricsErrorCodeNames.AggregateFailed);
        failed.Error.Category.ShouldBe("Metrics");
        failed.CausationId.ShouldBe(input.MessageId);
        observer.Values.Count.ShouldBe(3);
        var observed = observer.Values[2];
        observed.Exception.ShouldBeSameAs(original);
        observed.Exception.InnerException.ShouldBeSameAs(original.InnerException);
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.MessageId.ShouldBe(input.MessageId);
        observed.CorrelationId.ShouldBe(input.CorrelationId);
        observed.TraceId.ShouldBe(input.TraceId);
        foreach (var value in observer.Values)
        {
            value.ComponentType.ShouldBe(typeof(MetricsAggregateNode).FullName);
            (value.Activity is not null).ShouldBe(tracing);
            if (tracing) value.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        }

        await node.Input.SendAsync(input);
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        success.Value!.Json.Deserialize<MetricSnapshotOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.SampleCount.ShouldBe(1);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new MetricSampleInput { Group = "b", Value = 3 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).Error!.Code.ShouldBe(MetricsErrorCodeNames.GroupLimitReached);
        await node.Input.SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(3);
    }

    private sealed class FailingClock : TimeProvider
    {
        public Exception? NextFailure { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            if (NextFailure is { } exception)
            {
                NextFailure = null;
                throw exception;
            }
            return DateTimeOffset.UnixEpoch;
        }
    }

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        private readonly bool _throws;
        public Observer(bool throws)
        {
            _throws = throws;
            _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == FlowDiagnostics.ListenerName)
                _subscriptions.Add(value.Subscribe(this, name => name == FlowDiagnostics.ProcessingExceptionEvent));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            Values.Add((FlowProcessingException)value.Value!);
            if (_throws) throw new InvalidOperationException("observer failed");
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
