using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

public sealed class FlowValueObservabilityNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Counter_emits_counted_and_rejected_results_with_lineage()
    {
        var calls = 0;
        await using var node = new FlowValueCounterNode(
            new FlowValueCounterOptions
            {
                Name = "accepted",
                Predicate = "enabled"
            },
            new RecordingExpressionEngine((_, _, _) => ++calls > 1));
        var results = Sink(node.Output);
        var rejectedInput = FlowMessage.Create(FlowValue.From("first"));
        var acceptedInput = FlowMessage.Create(FlowValue.From("second"));

        await node.Input.SendAsync(rejectedInput);
        await node.Input.SendAsync(acceptedInput);

        var rejected = await results.ReceiveAsync().WaitAsync(Timeout);
        var counted = await results.ReceiveAsync().WaitAsync(Timeout);
        rejected.Payload.Kind.ShouldBe(ObservabilityResultKinds.CounterRejected);
        rejected.Payload.IsError.ShouldBeFalse();
        rejected.Payload.Value.ShouldNotBeNull().RejectedCount.ShouldBe(1);
        rejected.CorrelationId.ShouldBe(rejectedInput.CorrelationId);
        counted.Payload.Kind.ShouldBe(ObservabilityResultKinds.CounterSnapshot);
        counted.Payload.Value.ShouldNotBeNull().Count.ShouldBe(1);
        counted.Payload.Value.InputType.ShouldBe(nameof(FlowValue));
        counted.TraceId.ShouldBe(acceptedInput.TraceId);
        counted.CausationId.ShouldBe(acceptedInput.MessageId);
    }

    [Fact]
    public async Task Counter_predicate_failure_is_normal_and_later_input_continues()
    {
        var calls = 0;
        await using var node = new FlowValueCounterNode(
            new FlowValueCounterOptions { Predicate = "ok" },
            new RecordingExpressionEngine((_, _, _) =>
            {
                if (++calls == 1)
                    throw new InvalidOperationException("predicate failed");
                return true;
            }));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(1)));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(2)));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        var success = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.Payload.Kind.ShouldBe(ObservabilityResultKinds.CounterFailed);
        failure.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(ObservabilityErrorCodeNames.CounterPredicateFailed);
        success.Payload.Value.ShouldNotBeNull().Count.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Counter_output_fans_out_every_result()
    {
        await using var node = new FlowValueCounterNode(new FlowValueCounterOptions());
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("one")));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("two")));

        (await first.ReceiveAsync().WaitAsync(Timeout)).Payload.Value.ShouldNotBeNull().Count
            .ShouldBe(1);
        (await first.ReceiveAsync().WaitAsync(Timeout)).Payload.Value.ShouldNotBeNull().Count
            .ShouldBe(2);
        (await second.ReceiveAsync().WaitAsync(Timeout)).Payload.Value.ShouldNotBeNull().Count
            .ShouldBe(1);
        (await second.ReceiveAsync().WaitAsync(Timeout)).Payload.Value.ShouldNotBeNull().Count
            .ShouldBe(2);
    }

    [Fact]
    public async Task Logger_emits_flowvalue_attributes_and_renders_template()
    {
        var timestamp = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await using var node = new FlowValueLoggerNode(
            new FlowValueLoggerOptions
            {
                Category = "workflow.test",
                MessageTemplate = "{kind}:{sequence}",
                AttributeSelectors = ["kind"]
            },
            new Dictionary<string, IObservabilityFlowValueSelector>(StringComparer.Ordinal)
            {
                ["kind"] = new DelegateSelector((input, _) => input.GetObject()["kind"])
            },
            new FakeTimeProvider(timestamp));
        var results = Sink(node.Output);
        var input = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["kind"] = FlowValue.From("alpha")
        });

        await node.Input.SendAsync(FlowMessage.Create(input));

        var result = await results.ReceiveAsync().WaitAsync(Timeout);
        result.Payload.Kind.ShouldBe(ObservabilityResultKinds.LogEntry);
        var entry = result.Payload.Value.ShouldNotBeNull();
        entry.Timestamp.ShouldBe(timestamp);
        entry.Message.ShouldBe("alpha:1");
        entry.Input.ShouldBe(input);
        entry.Attributes.GetObject()["kind"].GetString().ShouldBe("alpha");
    }

    [Fact]
    public async Task Logger_selector_failures_are_one_partial_result_with_entry()
    {
        await using var node = new FlowValueLoggerNode(
            new FlowValueLoggerOptions { AttributeSelectors = ["good", "broken"] },
            new Dictionary<string, IObservabilityFlowValueSelector>(StringComparer.Ordinal)
            {
                ["good"] = new DelegateSelector((_, _) => FlowValue.From("kept")),
                ["broken"] = new DelegateSelector((_, _) =>
                    throw new InvalidOperationException("selector failed"))
            });
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("input")));

        var partial = await results.ReceiveAsync().WaitAsync(Timeout);
        partial.Payload.Kind.ShouldBe(ObservabilityResultKinds.LogEntryPartial);
        partial.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(ObservabilityErrorCodeNames.LoggerAttributeSelectorFailed);
        var attributes = partial.Payload.Value.ShouldNotBeNull().Attributes.GetObject();
        attributes["good"].GetString().ShouldBe("kept");
        attributes.ContainsKey("broken").ShouldBeFalse();
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Metrics_size_failure_is_partial_and_later_input_continues()
    {
        var calls = 0;
        await using var node = new FlowValueMetricsNode(
            new FlowValueMetricsOptions { Name = "items" },
            new DelegateSelector((_, _) =>
            {
                if (++calls == 1)
                    throw new InvalidOperationException("size failed");
                return FlowValue.From(3);
            }));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("first")));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("second")));

        var partial = await results.ReceiveAsync().WaitAsync(Timeout);
        var success = await results.ReceiveAsync().WaitAsync(Timeout);
        partial.Payload.Kind.ShouldBe(ObservabilityResultKinds.MetricSnapshotPartial);
        partial.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(ObservabilityErrorCodeNames.MetricsSizeSelectorFailed);
        partial.Payload.Value.ShouldNotBeNull().Count.ShouldBe(1);
        success.Payload.Kind.ShouldBe(ObservabilityResultKinds.MetricSnapshot);
        success.Payload.Value.ShouldNotBeNull().Count.ShouldBe(2);
        success.Payload.Value.TotalSize.ShouldBe(3);
    }

    [Fact]
    public async Task Metrics_non_finite_size_is_a_partial_result()
    {
        await using var node = new FlowValueMetricsNode(
            new FlowValueMetricsOptions(),
            new DelegateSelector((_, _) => FlowValue.From(double.NaN)));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("input")));

        var partial = await results.ReceiveAsync().WaitAsync(Timeout);
        partial.Payload.Kind.ShouldBe(ObservabilityResultKinds.MetricSnapshotPartial);
        partial.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(ObservabilityErrorCodeNames.MetricsSizeSelectorFailed);
        partial.Payload.Value.ShouldNotBeNull().Count.ShouldBe(1);
        partial.Payload.Value.TotalSize.ShouldBeNull();
    }

    [Fact]
    public async Task Normal_completion_drains_results_and_events()
    {
        await using var node = new FlowValueMetricsNode(new FlowValueMetricsOptions());
        var results = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("one")));
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        (await results.ReceiveAsync().WaitAsync(Timeout)).Payload.Kind
            .ShouldBe(ObservabilityResultKinds.MetricSnapshot);
        (await events.ReceiveAsync().WaitAsync(Timeout)).Name
            .ShouldBe(ObservabilityDiagnosticNames.MetricsObserved);
        results.TryReceive(out _).ShouldBeFalse();
        events.TryReceive(out _).ShouldBeFalse();
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class DelegateSelector(
        Func<FlowValue, ObservabilityNodeContext, FlowValue> selector)
        : IObservabilityFlowValueSelector
    {
        public FlowValue Select(FlowValue input, ObservabilityNodeContext context)
            => selector(input, context);
    }
}
