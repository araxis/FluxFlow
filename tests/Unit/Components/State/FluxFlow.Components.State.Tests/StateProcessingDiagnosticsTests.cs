using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.State.Tests;

[CollectionDefinition("State diagnostics", DisableParallelization = true)]
public sealed class StateDiagnosticsCollection;

[Collection("State diagnostics")]
public sealed class StateProcessingDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Expression_failure_preserves_original_exception_and_state(bool keyFailure, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        var engine = new Engine();
        await using var node = new StateReducerNode<long?>(new StateReducerOptions<long?>
        {
            Reducer = "sum",
            KeyExpression = keyFailure ? "key" : null,
            InitialState = 0
        }, engine);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { key = "counter", input = 1 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Input.SendAsync(input);
        (await output.ReceiveAsync(timeout)).Value!.Json.Deserialize<StateReducerResult<long?>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.NewState.ShouldBe(1);
        var original = new InvalidOperationException("expression failed", new ArgumentException("inner"));
        engine.NextFailure = original;
        await node.Input.SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.Error!.Code.ShouldBe(keyFailure ? StateErrorCodeNames.KeyEvaluationFailed : StateErrorCodeNames.ReducerFailed);
        failed.CausationId.ShouldBe(input.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        observed.Exception.InnerException.ShouldBeSameAs(original);
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        original.InnerException.ShouldBeOfType<ArgumentException>();
        observed.ComponentType.ShouldBe(typeof(StateReducerNode<long?>).FullName);
        observed.MessageId.ShouldBe(input.MessageId);
        observed.CorrelationId.ShouldBe(input.CorrelationId);
        observed.TraceId.ShouldBe(input.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Input.SendAsync(input);
        var result = (await output.ReceiveAsync(timeout)).Value!.Json.Deserialize<StateReducerResult<long?>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        result.PreviousState.ShouldBe(1);
        result.NewState.ShouldBe(2);
        result.Version.ShouldBe(2);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("invalid", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).IsError.ShouldBeTrue();
        await node.Input.SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(2);
        observer.Values[1].Exception.ShouldBeOfType<JsonException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Materializer_exception_is_reported_once_without_changing_rejection_or_state(bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        await using var node = new StateReducerNode<long?>(new StateReducerOptions<long?>
        {
            Reducer = "sum",
            InitialState = 0
        }, new Engine());
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("exception", new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Input.SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.Error!.Code.ShouldBe("state.reducer.invalid_shape");
        failed.CausationId.ShouldBe(input.MessageId);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.ShouldBeOfType<JsonException>();
        report.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        report.MessageId.ShouldBe(input.MessageId);
        report.CorrelationId.ShouldBe(input.CorrelationId);
        report.TraceId.ShouldBe(input.TraceId);
        report.ComponentType.ShouldBe(typeof(StateReducerNode<long?>).FullName);
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing) report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("rejection", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).Error!.Code.ShouldBe("state.reducer.invalid_shape");
        await node.Input.SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { key = "counter", input = 1 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var result = (await output.ReceiveAsync(timeout)).Value!.Json.Deserialize<StateReducerResult<long?>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        result.NewState.ShouldBe(1);
        result.Version.ShouldBe(1);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(2);
    }

    private sealed class Engine : IFlowExpressionEngine
    {
        public string Name => "test";
        public Exception? NextFailure { get; set; }
        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            if (NextFailure is { } exception)
            {
                NextFailure = null;
                throw exception;
            }
            return expression == "key" ? "counter"
                : Convert.ToInt64(context.Variables["state"]) + Convert.ToInt64(context.Variables["input"]);
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
