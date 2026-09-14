using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using System.Xml;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Payloads.Tests;

[CollectionDefinition("Payload diagnostics", DisableParallelization = true)]
public sealed class PayloadDiagnosticsCollection;

[Collection("Payload diagnostics")]
public sealed class PayloadDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Parse_failure_reports_once_while_format_probes_remain_normal(bool xml, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        await using var node = new PayloadInspectNode();
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var bad = Message(xml ? "<invalid" : "{invalid", xml ? "application/xml" : "application/json");
        await node.Input.SendAsync(bad);
        var failure = await output.ReceiveAsync(timeout);
        failure.Error!.Code.ShouldBe(PayloadErrorCodeNames.ParseFailed);
        failure.CausationId.ShouldBe(bad.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        observed.Exception.GetType().Name.ShouldBe("PayloadInspectionException");
        var original = observed.Exception.InnerException.ShouldNotBeNull();
        if (xml) original.ShouldBeOfType<XmlException>();
        else original.ShouldBeAssignableTo<JsonException>();
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(typeof(PayloadInspectNode).FullName);
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Input.SendAsync(Message(xml ? "<ok/>" : "{}", xml ? "application/xml" : "application/json"));
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        success.Value!.Json.Deserialize<PayloadInspectionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Kind.ShouldBe(xml ? PayloadKind.Xml : PayloadKind.JsonObject);
        // Both the invalid Base64 probe and invalid UTF-8 decoded preview are intentional fallbacks.
        await node.Input.SendAsync(Message("%%%%%%%%", "text/plain"));
        var text = await output.ReceiveAsync(timeout);
        text.IsError.ShouldBeFalse();
        text.Value!.Json.Deserialize<PayloadInspectionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Kind.ShouldBe(PayloadKind.Text);
        await node.Input.SendAsync(Message(Convert.ToBase64String(new byte[] { 255, 255, 255, 255, 255, 255 }), "text/plain"));
        var binaryPreview = await output.ReceiveAsync(timeout);
        binaryPreview.IsError.ShouldBeFalse();
        binaryPreview.Value!.Json.Deserialize<PayloadInspectionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Kind.ShouldBe(PayloadKind.Base64);
        await node.Input.SendAsync(failure);
        var forwarded = await output.ReceiveAsync(timeout);
        forwarded.Error.ShouldBeSameAs(failure.Error);
        forwarded.CausationId.ShouldBe(failure.MessageId);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(1);
    }

    private static FlowMessage Message(string value, string mediaType)
        => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(FlowContent.FromBytes(Encoding.UTF8.GetBytes(value), mediaType), new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_content_reports_conversion_exception_and_continues(bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        await using var node = new PayloadInspectNode();
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Ports.Outputs["Output"].LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { formatVersion = 1, bytes = "%%%" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            correlationId: CorrelationId.New());
        await node.Ports.Inputs["Input"].SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.Error!.Code.ShouldBe("flow.value.invalid_content");
        failed.Error.Category.ShouldBe("Materialization");
        failed.CausationId.ShouldBe(input.MessageId);
        var report = observer.Values.ShouldHaveSingleItem();
        var exception = report.Exception.ShouldBeOfType<JsonException>();
        exception.InnerException.ShouldBeOfType<FormatException>().StackTrace.ShouldNotBeNullOrWhiteSpace();
        exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        failed.Error.Details!.Value.GetProperty("reason").GetString().ShouldBe(exception.Message);
        failed.Error.Details.Value.GetProperty("target").GetString().ShouldBe(nameof(FlowContent));
        report.MessageId.ShouldBe(input.MessageId);
        report.CorrelationId.ShouldBe(input.CorrelationId);
        report.TraceId.ShouldBe(input.TraceId);
        report.ComponentType.ShouldBe(typeof(PayloadInspectNode).FullName);
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing) report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        // A plain string still materializes as content, without a conversion report.
        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        success.Value!.Json.Deserialize<PayloadInspectionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();
        await node.Ports.Inputs["Input"].SendAsync(failed);
        var forwarded = await output.ReceiveAsync(timeout);
        forwarded.Error.ShouldBeSameAs(failed.Error);
        forwarded.CausationId.ShouldBe(failed.MessageId);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(1);
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
