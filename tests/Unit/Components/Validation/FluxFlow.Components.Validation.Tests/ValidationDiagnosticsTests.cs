using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Json.Schema;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Validation.Tests;

[CollectionDefinition("Validation diagnostics", DisableParallelization = true)]
public sealed class ValidationDiagnosticsCollection;

[Collection("Validation diagnostics")]
public sealed class ValidationDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selector_exception_reports_once_but_schema_rejection_is_normal(bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        var original = new InvalidOperationException("selector failed", new ArgumentException("inner"));
        await using var node = new JsonSchemaValidatorNode(JsonSchema.FromText("{\"type\":\"integer\"}"), new Selector(original));
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var bad = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await node.Input.SendAsync(bad);
        var failure = await output.ReceiveAsync(timeout);
        failure.Error!.Code.ShouldBe(ValidationErrorCodeNames.ValueSelectorFailed);
        failure.Error.Category.ShouldBe("Validation");
        failure.CausationId.ShouldBe(bad.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        observed.Exception.ShouldBeSameAs(original);
        observed.Exception.InnerException.ShouldBeSameAs(original.InnerException);
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(typeof(JsonSchemaValidatorNode).FullName);
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        success.Value!.Json.Deserialize<JsonSchemaValidationResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.IsValid.ShouldBeTrue();
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("not an integer", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var invalid = await output.ReceiveAsync(timeout);
        invalid.IsError.ShouldBeFalse();
        var result = invalid.Value!.Json.Deserialize<JsonSchemaValidationResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldNotBeEmpty();
        await node.Input.SendAsync(failure);
        var forwarded = await output.ReceiveAsync(timeout);
        forwarded.Error.ShouldBeSameAs(failure.Error);
        forwarded.CausationId.ShouldBe(failure.MessageId);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(1);
    }

    private sealed class Selector(Exception exception) : IJsonSchemaValueSelector
    {
        public JsonElement Select(JsonElement input, JsonSchemaValidatorContext context)
        {
            if (input.ValueKind == JsonValueKind.Number && input.GetInt32() == -1) throw exception;
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
