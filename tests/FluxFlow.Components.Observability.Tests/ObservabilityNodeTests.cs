using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

public sealed class ObservabilityNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Counter_emits_counted_and_rejected_results_with_lineage()
    {
        var calls = 0;
        await using var node = new FlowCounterNode(
            new FlowCounterOptions
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
        await using var node = new FlowCounterNode(
            new FlowCounterOptions { Predicate = "ok" },
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
        await using var node = new FlowCounterNode(new FlowCounterOptions());
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
    public async Task Counter_without_predicate_uses_clock_and_emits_correlated_event()
    {
        var timestamp = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        await using var node = new FlowCounterNode(
            new FlowCounterOptions { Name = "items" },
            clock: new FakeTimeProvider(timestamp));
        var results = Sink(node.Output);
        var events = Sink(node.Events);
        var input = FlowMessage.Create(FlowValue.From("one"));

        await node.Input.SendAsync(input);

        var snapshot = (await results.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        snapshot.Timestamp.ShouldBe(timestamp);
        snapshot.LastObservedAt.ShouldBe(timestamp);
        snapshot.InputType.ShouldBe(nameof(FlowValue));
        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Name.ShouldBe(ObservabilityDiagnosticNames.CounterIncremented);
        @event.CorrelationId.ShouldBe(input.CorrelationId);
        @event.Attributes["name"].ShouldBe("items");
        @event.Attributes["nodeType"].ShouldBe("metric.count");
    }

    [Fact]
    public async Task Counter_uses_supplied_context_factory()
    {
        await using var node = new FlowCounterNode(
            new FlowCounterOptions { Predicate = "accepted" },
            new RecordingExpressionEngine((_, context, _) => context.Variables["accepted"]),
            new DelegateContextFactory(input => new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["accepted"] = input.GetObject()["accepted"].GetBoolean()
                }
            }));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.FromObject(
            new Dictionary<string, FlowValue>(StringComparer.Ordinal)
            {
                ["accepted"] = FlowValue.From(true)
            })));

        var result = await results.ReceiveAsync().WaitAsync(Timeout);
        result.Payload.IsError.ShouldBeFalse();
        result.Payload.Value.ShouldNotBeNull().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Logger_emits_flowvalue_attributes_and_renders_template()
    {
        var timestamp = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await using var node = new FlowLoggerNode(
            new FlowLoggerOptions
            {
                Category = "workflow.test",
                MessageTemplate = "{kind}:{sequence}",
                AttributeSelectors = ["kind"]
            },
            new Dictionary<string, IObservabilityValueSelector>(StringComparer.Ordinal)
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
        await using var node = new FlowLoggerNode(
            new FlowLoggerOptions { AttributeSelectors = ["good", "broken"] },
            new Dictionary<string, IObservabilityValueSelector>(StringComparer.Ordinal)
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
    public async Task Logger_does_not_expand_substituted_placeholders_and_allows_no_selectors()
    {
        await using var node = new FlowLoggerNode(
            new FlowLoggerOptions
            {
                Category = "workflow.test",
                MessageTemplate = "Observed {input}"
            });
        var results = Sink(node.Output);
        var events = Sink(node.Events);
        var input = FlowMessage.Create(FlowValue.From("{category}"));

        await node.Input.SendAsync(input);

        var entry = (await results.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        entry.Message.ShouldBe("Observed {category}");
        entry.Attributes.GetObject().ShouldBeEmpty();
        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Name.ShouldBe(ObservabilityDiagnosticNames.LoggerEmitted);
        @event.CorrelationId.ShouldBe(input.CorrelationId);
        @event.Attributes["nodeType"].ShouldBe("log.write");
    }

    [Fact]
    public async Task Metrics_size_failure_is_partial_and_later_input_continues()
    {
        var calls = 0;
        await using var node = new FlowMetricsNode(
            new FlowMetricsOptions { Name = "items" },
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
        await using var node = new FlowMetricsNode(
            new FlowMetricsOptions(),
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
    public async Task Metrics_tracks_rate_and_averages_only_sized_observations()
    {
        var firstObservedAt = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(firstObservedAt);
        await using var node = new FlowMetricsNode(
            new FlowMetricsOptions { Name = "messages" },
            new DelegateSelector((input, _) => input.GetObject()["size"]),
            clock);
        var results = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.FromObject(
            new Dictionary<string, FlowValue> { ["size"] = FlowValue.Null })));
        var first = (await results.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        clock.Advance(TimeSpan.FromSeconds(2));
        var secondInput = FlowMessage.Create(FlowValue.FromObject(
            new Dictionary<string, FlowValue> { ["size"] = FlowValue.From(4) }));
        await node.Input.SendAsync(secondInput);
        var second = (await results.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();

        first.AverageSize.ShouldBeNull();
        second.Count.ShouldBe(2);
        second.LastSize.ShouldBe(4);
        second.TotalSize.ShouldBe(4);
        second.AverageSize.ShouldBe(4);
        second.CurrentRatePerSecond.ShouldBe(0.5d);
        second.AverageRatePerSecond.ShouldBe(1d);
        var firstEvent = await events.ReceiveAsync().WaitAsync(Timeout);
        var secondEvent = await events.ReceiveAsync().WaitAsync(Timeout);
        firstEvent.Name.ShouldBe(ObservabilityDiagnosticNames.MetricsObserved);
        secondEvent.CorrelationId.ShouldBe(secondInput.CorrelationId);
        secondEvent.Attributes["nodeType"].ShouldBe("metric.measure");
    }

    [Fact]
    public void Constructors_validate_required_dependencies_and_options()
    {
        Should.Throw<ArgumentNullException>(() => new FlowCounterNode(null!));
        Should.Throw<ArgumentNullException>(() => new FlowLoggerNode(null!));
        Should.Throw<ArgumentNullException>(() => new FlowMetricsNode(null!));
        Should.Throw<ArgumentNullException>(() => new FlowCounterNode(
            new FlowCounterOptions { Predicate = "accepted" }));
        Should.Throw<ArgumentOutOfRangeException>(() => new FlowCounterNode(
            new FlowCounterOptions { BoundedCapacity = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() => new FlowLoggerNode(
            new FlowLoggerOptions { BoundedCapacity = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() => new FlowMetricsNode(
            new FlowMetricsOptions { BoundedCapacity = 0 }));
        Should.Throw<InvalidOperationException>(() => new FlowLoggerNode(
            new FlowLoggerOptions { Level = "unsupported" }));
    }

    [Fact]
    public async Task Normal_completion_drains_results_and_events()
    {
        await using var node = new FlowMetricsNode(new FlowMetricsOptions());
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
        : IObservabilityValueSelector
    {
        public FlowValue Select(FlowValue input, ObservabilityNodeContext context)
            => selector(input, context);
    }

    private sealed class DelegateContextFactory(
        Func<FlowValue, FlowMapContext> factory)
        : IFlowMapContextFactory<FlowValue>
    {
        public FlowMapContext Create(FlowValue input) => factory(input);
    }
}
