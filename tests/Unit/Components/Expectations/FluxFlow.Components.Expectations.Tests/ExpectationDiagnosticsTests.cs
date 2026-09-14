using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Expectations.Tests;

[CollectionDefinition("Expectation diagnostics", DisableParallelization = true)]
public sealed class ExpectationDiagnosticsCollection;

[Collection("Expectation diagnostics")]
public sealed class ExpectationDiagnosticsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Rejection_reports_once_and_keeps_single_resolution(bool tracing, bool throws)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var clock = new FakeTimeProvider();
        await using var node = new EventExpectationNode(new() { TimeoutMilliseconds = 100 }, clock);
        var callbackOutsideLock = false;
        using var observer = new Observer(() =>
        {
            // A separate thread can take the node's gate while the callback is running.
            callbackOutsideLock = Task.Run(() => node.ObservedEventCount).Wait(Deadline);
            if (throws) throw new InvalidOperationException("observer failed");
        });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var bad = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Input.SendAsync(bad);
        var result = await output.ReceiveAsync(Deadline);
        result.Error!.Code.ShouldBe("projection.event.invalid_shape");
        result.Error.Category.ShouldBe("Projections");
        result.CausationId.ShouldBe(bad.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        observed.Exception.ShouldBeOfType<JsonException>();
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(typeof(EventExpectationNode).FullName);
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        callbackOutsideLock.ShouldBeTrue();
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(Match());
        clock.Advance(TimeSpan.FromMilliseconds(100));
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        observer.Values.Count.ShouldBe(1);
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("match")]
    [InlineData("upstream")]
    [InlineData("completion")]
    public async Task Other_resolutions_do_not_manufacture_exceptions(string winner)
    {
        var clock = new FakeTimeProvider();
        using var observer = new Observer(() => { });
        await using var node = new EventExpectationNode(new() { TimeoutMilliseconds = 100 }, clock);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        if (winner == "timeout") clock.Advance(TimeSpan.FromMilliseconds(100));
        else if (winner == "match") await node.Input.SendAsync(Match());
        else if (winner == "upstream") await node.Input.SendAsync(FlowMessage.CreateError(new("upstream", "failed", "test")));
        else node.Complete();
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBe(winner == "upstream");
        if (winner == "timeout") result.Value!.Json.Deserialize<EventExpectationResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.TimedOut.ShouldBeTrue();
        if (winner != "completion")
            await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        observer.Values.ShouldBeEmpty();
        output.TryReceive(out _).ShouldBeFalse();
    }

    private static FlowMessage Match() => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new ProjectionEvent
    {
        Type = "match",
        Source = "test",
        Timestamp = DateTimeOffset.UnixEpoch
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        private readonly Action _callback;
        public Observer(Action callback)
        {
            _callback = callback;
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
            _callback();
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
