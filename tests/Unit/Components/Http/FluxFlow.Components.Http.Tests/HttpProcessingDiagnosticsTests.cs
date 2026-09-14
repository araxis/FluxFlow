using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

[CollectionDefinition("HTTP diagnostics", DisableParallelization = true)]
public sealed class HttpDiagnosticsCollection;

[Collection("HTTP diagnostics")]
public sealed class HttpProcessingDiagnosticsTests
{
    [Theory]
    [InlineData("network", false)]
    [InlineData("network", true)]
    [InlineData("send", false)]
    [InlineData("send", true)]
    [InlineData("read", false)]
    [InlineData("read", true)]
    [InlineData("timeout", false)]
    [InlineData("timeout", true)]
    public async Task Handled_exceptions_report_once_and_requests_continue(string mode, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        Exception original = mode == "network" ? new HttpRequestException("network failed") : new InvalidOperationException("operation failed");
        var calls = 0;
        using var client = new HttpClient(new Handler(async token =>
        {
            if (++calls > 1) return new(HttpStatusCode.OK) { Content = new StringContent("ok") };
            if (mode == "timeout") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (mode == "read") return new(HttpStatusCode.OK) { Content = new FailingContent(original) };
            throw original;
        }));
        await using var node = new HttpClientNode(client, new() { DefaultTimeoutMilliseconds = mode == "timeout" ? 30 : null });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var input = Request();
        await node.Input.SendAsync(input);
        var failure = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        failure.Error!.Code.ShouldBe(mode switch
        {
            "network" => HttpErrorCodeNames.Network,
            "read" => HttpErrorCodeNames.ResponseReadFailed,
            "timeout" => HttpErrorCodeNames.Timeout,
            _ => HttpErrorCodeNames.SendFailed
        });
        failure.Error.Category.ShouldBe("HTTP");
        failure.CausationId.ShouldBe(input.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        if (mode == "timeout") observed.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        else observed.Exception.ShouldBeSameAs(original);
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(typeof(HttpClientNode).FullName);
        observed.MessageId.ShouldBe(input.MessageId);
        observed.CorrelationId.ShouldBe(input.CorrelationId);
        observed.TraceId.ShouldBe(input.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
        await node.Input.SendAsync(input);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        await node.Input.SendAsync(failure);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        observer.Values.Count.ShouldBe(1);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Nonexception_failures_do_not_manufacture_exception_reports()
    {
        using var observer = new Observer();
        using var client = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        await using var node = new HttpClientNode(client, new() { TreatNonSuccessStatusAsError = true });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        await node.Input.SendAsync(Request());
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).Error!.Code.ShouldBe(HttpErrorCodeNames.NonSuccessStatus);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new HttpClientRequest { Url = "https://example.test/", Timeout = TimeSpan.Zero }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).Error!.Code.ShouldBe(HttpErrorCodeNames.InvalidTimeout);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        observer.Values.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Materialization_failure_reports_original_exception_without_sending(bool invalidBody, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        var calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        }));
        await using var node = new HttpClientNode(client);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        var value = invalidBody
            ? ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { url = "https://example.test/", body = new { formatVersion = 1, bytes = "%%%" } }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            : ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var input = FlowMessage.Create(value, correlationId: CorrelationId.New());
        await node.Input.SendAsync(input);
        var failure = await output.ReceiveAsync(timeout);
        calls.ShouldBe(0);
        failure.Error!.Code.ShouldBe("http.request.invalid_shape");
        failure.Error.Category.ShouldBe("HTTP");
        failure.CausationId.ShouldBe(input.MessageId);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.ShouldBeOfType<JsonException>();
        report.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        if (invalidBody)
            report.Exception.InnerException.ShouldBeOfType<FormatException>().StackTrace.ShouldNotBeNullOrWhiteSpace();
        failure.Error.Details!.Value.GetProperty("reason").GetString().ShouldBe(report.Exception.Message);
        report.MessageId.ShouldBe(input.MessageId);
        report.CorrelationId.ShouldBe(input.CorrelationId);
        report.TraceId.ShouldBe(input.TraceId);
        report.ComponentType.ShouldBe(typeof(HttpClientNode).FullName);
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing) report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Input.SendAsync(failure);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failure);
        calls.ShouldBe(0);
        await node.Input.SendAsync(Request());
        (await output.ReceiveAsync(timeout)).IsError.ShouldBeFalse();
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        calls.ShouldBe(1);
        observer.Values.Count.ShouldBe(1);
    }

    private static FlowMessage Request() => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new HttpClientRequest { Url = "https://example.test/" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(cancellationToken);
    }

    private sealed class FailingContent(Exception exception) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(exception);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
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
