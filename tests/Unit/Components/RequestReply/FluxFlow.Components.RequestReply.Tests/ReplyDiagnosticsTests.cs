using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RequestReply.Tests;

[CollectionDefinition("Reply diagnostics", DisableParallelization = true)]
public sealed class ReplyDiagnosticsCollection;

[Collection("Reply diagnostics")]
public sealed class ReplyDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reply_failures_keep_context_and_other_callers_continue(bool callbackFailure, bool tracing)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        using var worker = new Activity("unrelated").Start();
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var output = new BufferBlock<FlowMessage>();
        using var link = bridge.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        await bridge.StartAsync();
        var first = new Context { ThrowReply = callbackFailure };
        await bridge.Incoming.SendAsync(first);
        var request = await output.ReceiveAsync(timeout);
        var response = request.With(callbackFailure ? ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("reply", new JsonSerializerOptions(JsonSerializerDefaults.Web)))) : ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await bridge.Responses.SendAsync(response);
        var report = await observer.Report.Task.WaitAsync(timeout);
        await first.Settled.Task.WaitAsync(timeout);
        report.MessageId.ShouldBe(response.MessageId);
        report.CorrelationId.ShouldBe(response.CorrelationId);
        report.TraceId.ShouldBe(response.TraceId);
        report.ComponentType.ShouldBe(bridge.GetType().FullName);
        report.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        if (callbackFailure)
        {
            report.Exception.ShouldBeSameAs(first.Exception);
            report.Exception.InnerException.ShouldBeOfType<ArgumentException>();
            first.ReplyCalls.ShouldBe(1);
            first.FailCalls.ShouldBe(0);
        }
        else
        {
            report.Exception.ShouldBeOfType<JsonException>();
            first.Error!.Message.ShouldStartWith("request.reply.invalid_shape:");
            first.ReplyCalls.ShouldBe(0);
            first.FailCalls.ShouldBe(1);
        }
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing)
        {
            report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
            ActivityContext.TryParse(response.Headers["traceparent"], null, out var parent).ShouldBeTrue();
            report.Activity.TraceId.ShouldBe(parent.TraceId);
            report.Activity.ParentSpanId.ShouldBe(parent.SpanId);
        }
        worker.Status.ShouldBe(ActivityStatusCode.Unset);
        Activity.Current.ShouldBeSameAs(worker);
        var second = new Context();
        await bridge.Incoming.SendAsync(second);
        var next = await output.ReceiveAsync(timeout);
        await bridge.Responses.SendAsync(next.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("ok", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await second.Settled.Task.WaitAsync(timeout);
        second.Response.ShouldBe("ok");
        bridge.Complete();
        await bridge.Completion.WaitAsync(timeout);
        observer.Count.ShouldBe(1);
    }

    private sealed class Context : IRequestContext<string, string>
    {
        public string Request => "request";
        public CorrelationId? CorrelationId => null;
        public ActivityContext TraceContext => new(
            ActivityTraceId.CreateFromString("1234567890abcdef1234567890abcdef"),
            ActivitySpanId.CreateFromString("1234567890abcdef"), ActivityTraceFlags.Recorded);
        public bool ThrowReply { get; init; }
        public Exception Exception { get; } = new InvalidOperationException("reply failed", new ArgumentException("inner"));
        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReplyCalls { get; private set; }
        public int FailCalls { get; private set; }
        public string? Response { get; private set; }
        public Exception? Error { get; private set; }
        public Task ReplyAsync(string response, CancellationToken cancellationToken = default)
        {
            ReplyCalls++;
            Response = response;
            Settled.TrySetResult();
            if (ThrowReply) throw Exception;
            return Task.CompletedTask;
        }
        public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        { FailCalls++; Error = error; Settled.TrySetResult(); return Task.CompletedTask; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public TaskCompletionSource<FlowProcessingException> Report { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count;
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
            Interlocked.Increment(ref Count);
            Report.TrySetResult((FlowProcessingException)value.Value!);
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
