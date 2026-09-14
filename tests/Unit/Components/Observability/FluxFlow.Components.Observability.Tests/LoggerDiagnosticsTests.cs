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

[CollectionDefinition("Logger diagnostics", DisableParallelization = true)]
public sealed class LoggerDiagnosticsCollection;

[Collection("Logger diagnostics")]
public sealed class LoggerDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Each_selector_exception_is_preserved_in_one_workflow_failure(bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        var first = new InvalidOperationException("first failed", new ArgumentException("inner"));
        var second = new FormatException("second failed");
        await using var node = new FlowLoggerNode<int>(new FlowLoggerOptions { AttributeSelectors = ["first", "second"] },
            new Dictionary<string, IObservabilityValueSelector<int>>
            {
                ["first"] = new Selector(first),
                ["second"] = new Selector(second)
            });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var bad = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Input.SendAsync(bad);
        var failure = await output.ReceiveAsync(timeout);
        failure.Error!.Code.ShouldBe(ObservabilityErrorCodeNames.LoggerAttributeSelectorFailed);
        failure.Error.Details.ShouldNotBeNull().GetProperty("failedSelectors").EnumerateArray()
            .Select(value => value.GetString()).ShouldBe(new[] { "first", "second" });
        failure.CausationId.ShouldBe(bad.MessageId);
        observer.Values.Count.ShouldBe(2);
        observer.Values[0].Exception.ShouldBeSameAs(first);
        observer.Values[1].Exception.ShouldBeSameAs(second);
        first.InnerException.ShouldBeOfType<ArgumentException>();
        foreach (var observed in observer.Values)
        {
            observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
            observed.ComponentType.ShouldBe(typeof(FlowLoggerNode<int>).FullName);
            observed.MessageId.ShouldBe(bad.MessageId);
            observed.CorrelationId.ShouldBe(bad.CorrelationId);
            observed.TraceId.ShouldBe(bad.TraceId);
            (observed.Activity is not null).ShouldBe(tracing);
            if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        }
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("invalid", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).Error!.Code.ShouldBe(ObservabilityErrorCodeNames.LoggerFailed);
        observer.Values.Count.ShouldBe(3);
        observer.Values[2].Exception.ShouldBeOfType<JsonException>();
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(7, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        var entry = success.Value!.Json.Deserialize<FlowLogEntry<int>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        entry.Sequence.ShouldBe(2);
        entry.Attributes.Count.ShouldBe(2);
        entry.Input.ShouldBe(7);
        await node.Input.SendAsync(failure);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(3);
        output.TryReceive(out _).ShouldBeFalse();
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
