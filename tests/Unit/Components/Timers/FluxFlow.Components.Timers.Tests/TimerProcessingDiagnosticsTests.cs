using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

[CollectionDefinition("Timer diagnostics", DisableParallelization = true)]
public sealed class TimerDiagnosticsCollection;

[Collection("Timer diagnostics")]
public sealed class TimerProcessingDiagnosticsTests
{
    [Theory]
    [InlineData("delay", false, false)]
    [InlineData("delay", true, false)]
    [InlineData("throttle", false, false)]
    [InlineData("throttle", true, false)]
    [InlineData("debounce", false, false)]
    [InlineData("debounce", true, false)]
    [InlineData("delay", false, true)]
    [InlineData("delay", true, true)]
    [InlineData("debounce", false, true)]
    [InlineData("debounce", true, true)]
    public async Task Processing_failure_reports_once_and_later_input_succeeds(string kind, bool tracing, bool deferred)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new Observer();
        using var worker = new Activity("unrelated-worker").Start();
        var clock = new FailingClock(kind == "debounce" && !deferred, deferred && kind == "delay" ? 2 : 1);
        await using FlowNode node = kind switch
        {
            "delay" => new TimerDelayNode(new() { Delay = TimeSpan.Zero }, clock),
            "throttle" => new TimerThrottleNode(new() { Interval = TimeSpan.FromMilliseconds(1) }, clock),
            _ => new TimerDebounceNode(new() { QuietPeriod = TimeSpan.FromMilliseconds(1) }, clock)
        };
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("value", new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New(),
            headers: new Dictionary<string, string>
            {
                ["traceparent"] = "00-1234567890abcdef1234567890abcdef-1234567890abcdef-01"
            });
        var timeout = TimeSpan.FromSeconds(5);
        await node.Input.SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.Error!.Code.ShouldBe($"timer.{kind}_failed");
        failed.CausationId.ShouldBe(input.MessageId);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.ShouldBeSameAs(clock.Failure);
        report.Exception.InnerException.ShouldBeOfType<ArgumentException>();
        report.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        report.ComponentType.ShouldBe(node.GetType().FullName);
        report.MessageId.ShouldBe(input.MessageId);
        report.CorrelationId.ShouldBe(input.CorrelationId);
        report.TraceId.ShouldBe(input.TraceId);
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing)
        {
            report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
            report.Activity.TraceId.ToString().ShouldBe("1234567890abcdef1234567890abcdef");
            report.Activity.ParentSpanId.ToString().ShouldBe("1234567890abcdef");
        }
        worker.Status.ShouldBe(ActivityStatusCode.Unset);
        Activity.Current.ShouldBeSameAs(worker);
        await node.Input.SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        await node.Input.SendAsync(input);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(input);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(1);
    }

    private sealed class FailingClock(bool timer, int failOnRead) : TimeProvider
    {
        private bool _failed;
        private int _reads;
        public Exception Failure { get; } = new InvalidOperationException("clock failed", new ArgumentException("inner"));
        public override DateTimeOffset GetUtcNow()
        {
            if (!timer && !_failed && ++_reads == failOnRead) { _failed = true; throw Failure; }
            return TimeProvider.System.GetUtcNow();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (timer && !_failed) { _failed = true; throw Failure; }
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
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
