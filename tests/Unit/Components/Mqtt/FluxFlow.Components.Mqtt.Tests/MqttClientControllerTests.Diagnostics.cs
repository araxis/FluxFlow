using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

[CollectionDefinition("MQTT processing diagnostics", DisableParallelization = true)]
public sealed class MqttProcessingDiagnosticsCollection;

[Collection("MQTT processing diagnostics")]
public sealed partial class MqttClientControllerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OperationDiagnosticsPreserveTransportExceptionAndContinue(bool control, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new ProcessingObserver();
        var original = new MqttTransportException("publish denied", "Authentication", isTransient: false);
        var calls = 0;
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = (_, _) => ++calls == 1 ? ValueTask.FromException(original) : ValueTask.CompletedTask
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using IFlowComponent node = control ? new MqttControlNode(controller) : new MqttPublishOperationNode(controller);
        var output = MqttTestContext.Sink(node.Ports.Outputs["Output"]);
        var publish = Publish("events/one");
        var input = FlowMessage.Create(control
            ? MqttRequest(new MqttPublishClientRequest { Message = publish }).Value!
            : ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(publish, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), correlationId: CorrelationId.New());
        await node.Ports.Inputs["Input"].SendAsync(input);
        var failure = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        failure.Error!.Code.ShouldBe(MqttClientErrorCodes.PublishFailed);
        failure.Error.IsTransient.ShouldBeFalse();
        failure.CausationId.ShouldBe(input.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        var wrapper = observed.Exception.ShouldBeOfType<MqttClientOperationException>();
        wrapper.InnerException.ShouldBeSameAs(original);
        wrapper.Error.ShouldBeSameAs(failure.Error);
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(node.GetType().FullName);
        observed.MessageId.ShouldBe(input.MessageId);
        observed.CorrelationId.ShouldBe(input.CorrelationId);
        observed.TraceId.ShouldBe(input.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        await node.Ports.Inputs["Input"].SendAsync(input);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeTrue();
        await node.Ports.Inputs["Input"].SendAsync(failure);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        observer.Values.Count.ShouldBe(2);
        observer.Values[1].Exception.ShouldBeOfType<JsonException>();
        calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MaterializationDiagnosticsPreserveContentExceptionWithoutPublishing(bool control, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new ProcessingObserver();
        var calls = 0;
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = (_, _) => { calls++; return ValueTask.CompletedTask; }
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using IFlowComponent node = control ? new MqttControlNode(controller) : new MqttPublishOperationNode(controller);
        var output = MqttTestContext.Sink(node.Ports.Outputs["Output"]);
        var invalid = new { topic = "events/one", content = new { formatVersion = 1, bytes = "%%%" } };
        var value = control
            ? ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Operation"] = "Publish", ["message"] = invalid }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            : ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(invalid, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var input = FlowMessage.Create(value, correlationId: CorrelationId.New());
        var timeout = TimeSpan.FromSeconds(5);
        await node.Ports.Inputs["Input"].SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.Error!.Code.ShouldBe(control ? "mqtt.command.invalid_shape" : "mqtt.publish.invalid_shape");
        failed.Error.Category.ShouldBe("MQTT");
        failed.CausationId.ShouldBe(input.MessageId);
        calls.ShouldBe(0);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.ShouldBeOfType<JsonException>();
        report.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        report.Exception.InnerException.ShouldBeOfType<FormatException>().StackTrace.ShouldNotBeNullOrWhiteSpace();
        failed.Error.Details!.Value.GetProperty("reason").GetString().ShouldBe(report.Exception.Message);
        report.MessageId.ShouldBe(input.MessageId);
        report.CorrelationId.ShouldBe(input.CorrelationId);
        report.TraceId.ShouldBe(input.TraceId);
        report.ComponentType.ShouldBe(node.GetType().FullName);
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing) report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        await node.Ports.Inputs["Input"].SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        calls.ShouldBe(0);
        var publish = Publish("events/one");
        var good = control ? MqttRequest(new MqttPublishClientRequest { Message = publish }) : FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(publish, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await node.Ports.Inputs["Input"].SendAsync(good);
        (await output.ReceiveAsync(timeout)).IsError.ShouldBeFalse();
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        calls.ShouldBe(1);
        observer.Values.Count.ShouldBe(1);
    }

    [Fact]
    public void ExistingOperationExceptionConstructorPreservesErrorWithoutInnerException()
    {
        var error = new FlowError("mqtt.failed", "failed", "MQTT");
        var exception = new MqttClientOperationException(MqttClientOperation.Publish, error);
        exception.InnerException.ShouldBeNull();
        exception.Error.ShouldBeSameAs(error);
        exception.Message.ShouldBe(error.Message);
    }

    private sealed class ProcessingObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        public ProcessingObserver() => _discovery = DiagnosticListener.AllListeners.Subscribe(this);
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
