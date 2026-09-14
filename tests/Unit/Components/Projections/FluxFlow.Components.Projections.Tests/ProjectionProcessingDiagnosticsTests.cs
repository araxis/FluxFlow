using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Projections.Tests;

[CollectionDefinition("Projection diagnostics", DisableParallelization = true)]
public sealed class ProjectionDiagnosticsCollection;

[Collection("Projection diagnostics")]
public sealed class ProjectionProcessingDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Caught_failures_report_once_without_changing_filtering_or_continuation(bool tracing, bool throwingObserver)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer(throwingObserver);
        await using var node = new EventProjectionNode(new EventProjectionOptions
        {
            Filter = new EventFilter { Type = "match" }
        });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).Error!.Code.ShouldBe("projection.event.invalid_shape");
        observer.Values.ShouldHaveSingleItem().Exception.ShouldBeOfType<JsonException>();

        // Subtracting the rate window from MinValue exercises a real local processing failure.
        var bad = Message(DateTimeOffset.MinValue, "match");
        await node.Input.SendAsync(bad);
        var failure = await output.ReceiveAsync(timeout);
        failure.Error!.Code.ShouldBe(ProjectionErrorCodeNames.ProjectionFailed);
        failure.Error.Category.ShouldBe("Projections");
        failure.CausationId.ShouldBe(bad.MessageId);
        observer.Values.Count.ShouldBe(2);
        var observed = observer.Values[1];
        observed.Exception.ShouldBeOfType<ArgumentOutOfRangeException>();
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        foreach (var value in observer.Values)
        {
            value.ComponentType.ShouldBe(typeof(EventProjectionNode).FullName);
            (value.Activity is not null).ShouldBe(tracing);
            if (tracing) value.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        }

        await node.Input.SendAsync(Message(DateTimeOffset.UnixEpoch, "ignored"));
        var good = Message(DateTimeOffset.UnixEpoch, "match");
        await node.Input.SendAsync(good);
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        success.CausationId.ShouldBe(good.MessageId);
        var snapshot = success.Value!.Json.Deserialize<EventProjectionSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        snapshot.Latest!.Type.ShouldBe("match");
        // Existing counters include the matched event whose rate calculation failed.
        snapshot.ObservedCount.ShouldBe(3);
        snapshot.MatchedCount.ShouldBe(2);
        await node.Input.SendAsync(failure);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(2);
        output.TryReceive(out _).ShouldBeFalse();
    }

    private static FlowMessage Message(DateTimeOffset timestamp, string type)
        => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new ProjectionEvent
        {
            Timestamp = timestamp,
            Type = type,
            Source = "test"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());

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
