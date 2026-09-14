using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

[CollectionDefinition("Session diagnostics", DisableParallelization = true)]
public sealed class SessionDiagnosticsCollection;

[Collection("Session diagnostics")]
public sealed partial class SessionComponentTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Processing_diagnostics_preserve_local_errors_and_continuation(bool record, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var diagnostics = new ProcessingObserver();
        var store = new TestSessionStore { FailNextAppend = record, FailNextQuery = !record };
        await using IFlowComponent node = record
            ? new SessionRecorderNode(new SessionRecorderOptions { SessionId = "diagnostics" }, store)
            : new SessionQueryNode(new SessionQueryOptions(), store);
        var output = Sink(node.Ports.Outputs["Output"]);
        var input = record ? SessionRequest(ContentInput(1, "first")) : SessionRequest(new SessionQueryRequest());
        await node.Ports.Inputs["Input"].SendAsync(input);
        var failure = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        failure.Error!.Code.ShouldBe(record ? SessionErrorCodeNames.RecordFailed : SessionErrorCodeNames.QueryFailed);
        failure.Error.Category.ShouldBe("Sessions");
        failure.CausationId.ShouldBe(input.MessageId);
        var observed = diagnostics.Values.ShouldHaveSingleItem();
        observed.Exception.ShouldBeOfType<InvalidOperationException>();
        observed.Exception.Message.ShouldBe(record ? "append failed" : "query failed");
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(node.GetType().FullName);
        observed.MessageId.ShouldBe(input.MessageId);
        observed.CorrelationId.ShouldBe(input.CorrelationId);
        observed.TraceId.ShouldBe(input.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var malformed = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        malformed.Error!.Code.ShouldBe($"sessions.{(record ? "record" : "query")}.invalid_shape");
        diagnostics.Values.Count.ShouldBe(2);
        diagnostics.Values[1].Exception.ShouldBeOfType<JsonException>();
        if (tracing) diagnostics.Values[1].Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Ports.Inputs["Input"].SendAsync(input);
        var success = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        success.IsError.ShouldBeFalse();
        if (record) success.Value!.Json.Deserialize<SessionContentRecord>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Sequence.ShouldBe(1);
        await node.Ports.Inputs["Input"].SendAsync(failure);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        if (node is SessionRecorderNode recorder)
            await recorder.SessionCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        diagnostics.Values.Count.ShouldBe(2);
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
        public void OnNext(KeyValuePair<string, object?> value) => Values.Add((FlowProcessingException)value.Value!);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
