using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serialization.Tests;

[CollectionDefinition("Serialization diagnostics", DisableParallelization = true)]
public sealed class SerializationDiagnosticsCollection;

[Collection("Serialization diagnostics")]
public sealed class SerializationDiagnosticsTests
{
    [Fact]
    public async Task Lazy_json_failure_is_reported_once_and_next_input_succeeds()
    {
        using var observer = new Observer();
        var exception = new JsonException("Cannot create JSON");
        await using var node = new Base64DecodeNode();
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new FailingValue(exception)));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("aGVsbG8="))));
        var failed = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        failed.Error!.Code.ShouldBe("flow.value.expected_string");
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        observer.Values.ShouldHaveSingleItem().Exception.ShouldBeSameAs(exception);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FailingValue(Exception exception) : FlowValue
    {
        protected override JsonElement CreateJson() => throw exception;
    }

    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("base64", false)]
    [InlineData("base64", true)]
    public async Task Converter_failure_retains_inner_exception_and_continues(string kind, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        await using IFlowComponent node = kind switch
        {
            "json" => new JsonParseNode(),
            "text" => new TextDecodeNode(new() { MaxInputBytes = 2 }),
            _ => new Base64DecodeNode()
        };
        var badValue = kind switch
        {
            "json" => Content(Encoding.UTF8.GetBytes("{invalid")),
            "text" => Content(new byte[] { 1, 2, 3 }),
            _ => ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("%%%", new JsonSerializerOptions(JsonSerializerDefaults.Web))))
        };
        var bad = FlowMessage.Create(badValue, correlationId: CorrelationId.New());
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Ports.Outputs["Output"].LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        await node.Ports.Inputs["Input"].SendAsync(bad);
        var failure = await output.ReceiveAsync(timeout);
        failure.Error!.Code.ShouldBe(kind switch
        {
            "json" => SerializationErrorCodeNames.JsonParseFailed,
            "text" => SerializationErrorCodeNames.InputTooLarge,
            _ => SerializationErrorCodeNames.Base64DecodeFailed
        });
        failure.Error.Category.ShouldBe("Serialization");
        failure.CausationId.ShouldBe(bad.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        observed.Exception.GetType().Name.ShouldBe("SerializationFailureException");
        if (kind == "text") observed.Exception.InnerException.ShouldBeNull();
        else
        {
            var original = observed.Exception.InnerException.ShouldNotBeNull();
            if (kind == "json") original.ShouldBeAssignableTo<JsonException>();
            else original.ShouldBeOfType<FormatException>();
            original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        }
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldContain("SerializationPipeline");
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        var good = kind switch
        {
            "json" => Content(Encoding.UTF8.GetBytes("{\"ok\":true}")),
            "text" => Content(Encoding.UTF8.GetBytes("ok")),
            _ => ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("AQI=", new JsonSerializerOptions(JsonSerializerDefaults.Web))))
        };
        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create(good));
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        if (kind == "json") success.Value!.Json.GetProperty("ok").GetBoolean().ShouldBeTrue();
        else if (kind == "text") success.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("ok");
        else success.Value!.Json.Deserialize<FlowContent>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Bytes.ToArray().ShouldBe(new byte[] { 1, 2 });
        await node.Ports.Inputs["Input"].SendAsync(failure);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(1);
    }

    private static FlowValue Content(byte[] bytes) => ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(FlowContent.FromBytes(bytes), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("base64", false)]
    [InlineData("base64", true)]
    public async Task Invalid_content_reports_conversion_exception_and_continues(string kind, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        await using IFlowComponent node = kind switch
        {
            "json" => new JsonParseNode(),
            "text" => new TextDecodeNode(),
            _ => new Base64EncodeNode()
        };
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
        report.ComponentType.ShouldContain("SerializationPipeline");
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing) report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        // A plain string still materializes as content, without a conversion report.
        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var success = await output.ReceiveAsync(timeout);
        success.IsError.ShouldBeFalse();
        if (kind == "json") success.Value!.Json.ValueKind.ShouldBe(JsonValueKind.Object);
        else success.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(kind == "text" ? "{}" : "e30=");
        await node.Ports.Inputs["Input"].SendAsync(failed);
        var forwarded = await output.ReceiveAsync(timeout);
        forwarded.Error.ShouldBeSameAs(failed.Error);
        forwarded.ShouldBeSameAs(failed);
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
