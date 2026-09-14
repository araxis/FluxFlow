using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

[CollectionDefinition("Routing diagnostics", DisableParallelization = true)]
public sealed class RoutingDiagnosticsCollection;

[Collection("Routing diagnostics")]
public sealed class RoutingProcessingDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Window_reports_conversion_failure_before_queued_error_and_continues(bool tracing)
    {
        using var listener = Listen(tracing);
        using var observer = new Observer();
        await using var node = new WindowNode<int>(new() { MaxItems = 2 });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("invalid", new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        var timeout = TimeSpan.FromSeconds(5);
        await node.Input.SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.IsError.ShouldBeTrue();
        failed.CausationId.ShouldBe(input.MessageId);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.ShouldBeOfType<JsonException>().StackTrace.ShouldNotBeNullOrWhiteSpace();
        CheckReport(report, input, node, tracing);
        await node.Input.SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(10, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(20, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var window = (await output.ReceiveAsync(timeout)).Value!.Json.Deserialize<FlowWindow<int>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        window.Items.ShouldBe(new[] { 10, 20 });
        window.Sequence.ShouldBe(1);
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Correlation_reports_selectors_outside_gate_and_continues(bool sideFailure, bool tracing)
    {
        using var listener = Listen(tracing);
        using var observer = new Observer();
        var original = new InvalidOperationException("selector failed", new ArgumentException("inner"));
        await using var node = new CorrelationNode<Item>(new(),
            item => item.Fail && !sideFailure ? throw original : item.Key,
            item => item.Fail && sideFailure ? throw original : item.Side);
        var gate = typeof(CorrelationNode<Item>).GetField("_gate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(node)!;
        observer.BeforeReport = () => observer.GateWasFree = Task.Run(() =>
        {
            if (!Monitor.TryEnter(gate, TimeSpan.FromSeconds(1))) return false;
            Monitor.Exit(gate);
            return true;
        }).GetAwaiter().GetResult();
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", "request", true), new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        var timeout = TimeSpan.FromSeconds(5);
        await node.Input.SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.IsError.ShouldBeTrue();
        failed.CausationId.ShouldBe(input.MessageId);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.InnerException.ShouldBeSameAs(original);
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        original.InnerException.ShouldBeOfType<ArgumentException>();
        observer.GateWasFree.ShouldBeTrue();
        CheckReport(report, input, node, tracing);
        await node.Input.SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", "request"), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", "response"), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var matched = (await output.ReceiveAsync(timeout)).Value!.Json.Deserialize<FlowCorrelationMatchedOutcome<Item>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        matched.Match.Key.ShouldBe("key");
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Correlation_keeps_selector_failure_when_final_scheduling_also_fails()
    {
        using var observer = new Observer();
        var original = new InvalidOperationException("selector");
        var clock = new SchedulingClock();
        await using var node = new CorrelationNode<Item>(new(),
            item => item.Fail ? throw original : item.Key, item => item.Side, clock: clock);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var timeout = TimeSpan.FromSeconds(5);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", "request", true), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).IsError.ShouldBeTrue();
        observer.Values.Count.ShouldBe(2);
        observer.Values[0].Exception.InnerException.ShouldBeSameAs(original);
        observer.Values[1].Exception.ShouldBeSameAs(clock.Failure);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", "request"), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", "response"), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).IsError.ShouldBeFalse();
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(2);
    }

    private sealed class SchedulingClock : TimeProvider
    {
        private int _reads;
        public Exception Failure { get; } = new InvalidOperationException("schedule clock");
        public override DateTimeOffset GetUtcNow()
        {
            if (++_reads == 2) throw Failure;
            return TimeProvider.System.GetUtcNow();
        }
    }

    public sealed record Item(string Key, string Side, bool Fail = false);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Join_reports_at_command_boundary_and_continues(bool rightFailure, bool tracing)
    {
        using var listener = Listen(tracing);
        using var observer = new Observer();
        var original = new InvalidOperationException("selector failed", new ArgumentException("inner"));
        await using var node = new JoinNode<Item, Item>(new(),
            item => item.Fail && !rightFailure ? throw original : item.Key,
            item => item.Fail && rightFailure ? throw original : item.Key);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        var target = rightFailure ? node.Right : node.Left;
        var timeout = TimeSpan.FromSeconds(5);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", "", true), new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        await target.SendAsync(input);
        var failed = await output.ReceiveAsync(timeout);
        failed.IsError.ShouldBeTrue();
        failed.CausationId.ShouldBe(input.MessageId);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.InnerException.ShouldBeSameAs(original);
        original.StackTrace.ShouldNotBeNullOrWhiteSpace();
        CheckReport(report, input, node, tracing);
        await target.SendAsync(failed);
        (await output.ReceiveAsync(timeout)).ShouldBeSameAs(failed);
        await target.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await output.ReceiveAsync(timeout)).IsError.ShouldBeTrue();
        observer.Values.Count.ShouldBe(2);
        observer.Values[1].Exception.ShouldBeOfType<JsonException>();
        await node.Left.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", ""), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await node.Right.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", ""), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var match = (await output.ReceiveAsync(timeout)).Value!.Json.Deserialize<FlowJoinMatchedOutcome<Item, Item>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        match.Match.Key.ShouldBe("key");
        node.Complete();
        await node.Completion.WaitAsync(timeout);
        observer.Values.Count.ShouldBe(2);
    }

    private static ActivityListener Listen(bool tracing)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static void CheckReport(FlowProcessingException report, FlowMessage input, object node, bool tracing)
    {
        report.MessageId.ShouldBe(input.MessageId);
        report.CorrelationId.ShouldBe(input.CorrelationId);
        report.TraceId.ShouldBe(input.TraceId);
        report.ComponentType.ShouldBe(node.GetType().FullName);
        (report.Activity is not null).ShouldBe(tracing);
        if (tracing) report.Activity!.Status.ShouldBe(ActivityStatusCode.Error);
    }

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        public Action? BeforeReport { get; set; }
        public bool GateWasFree { get; set; }
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
            BeforeReport?.Invoke();
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
