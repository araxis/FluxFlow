using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Assertions.Tests;

[CollectionDefinition("Assertion diagnostics", DisableParallelization = true)]
public sealed class AssertionDiagnosticsCollection;

[Collection("Assertion diagnostics")]
public sealed class AssertionDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conversion_and_expression_failures_report_once_and_continue(bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        var original = new InvalidOperationException("predicate failed", new ArgumentException("inner"));
        await using var node = new AssertionNode<int>(new AssertionOptions { Expression = "positive" }, new Engine(original));
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("invalid", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).Error!.Code.ShouldBe(AssertionErrorCodeNames.EvaluationFailed);
        observer.Values.ShouldHaveSingleItem().Exception.ShouldBeOfType<JsonException>();
        var bad = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Input.SendAsync(bad);
        var failure = await output.ReceiveAsync(timeout);
        failure.Error!.Code.ShouldBe(AssertionErrorCodeNames.EvaluationFailed);
        failure.Error.Category.ShouldBe("Assertions");
        failure.CausationId.ShouldBe(bad.MessageId);
        observer.Values.Count.ShouldBe(2);
        var observed = observer.Values[1];
        observed.Exception.ShouldBeSameAs(original);
        observed.Exception.InnerException.ShouldBeSameAs(original.InnerException);
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        foreach (var value in observer.Values)
        {
            value.ComponentType.ShouldBe(typeof(AssertionNode<int>).FullName);
            (value.Activity is not null).ShouldBe(tracing);
            if (tracing) value.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        }
        var good = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await node.Input.SendAsync(good);
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        success.Value!.Json.Deserialize<FluxFlow.Components.Assertions.Contracts.AssertionResult<int>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Passed.ShouldBeTrue();
        success.CausationId.ShouldBe(good.MessageId);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(0, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var negative = await output.ReceiveAsync(timeout);
        negative.IsError.ShouldBeFalse();
        negative.Value!.Json.Deserialize<FluxFlow.Components.Assertions.Contracts.AssertionResult<int>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Passed.ShouldBeFalse();
        await node.Input.SendAsync(failure);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(2);
    }

    private sealed class Engine(Exception failure) : IFlowExpressionEngine
    {
        public string Name => "test";
        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            var input = (int)context.Variables["input"]!;
            if (input < 0) throw failure;
            return input > 0;
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
