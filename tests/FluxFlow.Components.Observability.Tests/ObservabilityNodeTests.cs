using System.Text.Json;
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
    public async Task Counter_emits_counted_and_rejected_snapshots_with_lineage()
    {
        var calls = 0;
        await using var node = new FlowCounterNode(
            new FlowCounterOptions { Name = "accepted", Predicate = "enabled" },
            new RecordingExpressionEngine((_, _, _) => ++calls > 1));
        var results = Sink(node.Output);
        var rejectedInput = FlowMessage.Create(Json("first"));
        var acceptedInput = FlowMessage.Create(Json("second"));

        await node.Input.SendAsync(rejectedInput);
        await node.Input.SendAsync(acceptedInput);

        var rejected = await results.ReceiveAsync().WaitAsync(Timeout);
        var counted = await results.ReceiveAsync().WaitAsync(Timeout);
        rejected.IsError.ShouldBeFalse();
        rejected.Value.RejectedCount.ShouldBe(1);
        rejected.CorrelationId.ShouldBe(rejectedInput.CorrelationId);
        counted.Value.Count.ShouldBe(1);
        counted.Value.InputType.ShouldBe(typeof(JsonElement).FullName);
        counted.TraceId.ShouldBe(acceptedInput.TraceId);
        counted.CausationId.ShouldBe(acceptedInput.MessageId);
    }

    [Fact]
    public async Task Counter_predicate_failure_is_in_band_and_later_input_continues()
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

        await node.Input.SendAsync(FlowMessage.Create(Json(1)));
        await node.Input.SendAsync(FlowMessage.Create(Json(2)));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        var success = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(ObservabilityErrorCodeNames.CounterPredicateFailed);
        success.Value.Count.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Counter_output_fans_out_every_snapshot()
    {
        await using var node = new FlowCounterNode(new FlowCounterOptions());
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Json("one")));
        await node.Input.SendAsync(FlowMessage.Create(Json("two")));

        (await first.ReceiveAsync().WaitAsync(Timeout)).Value.Count.ShouldBe(1);
        (await first.ReceiveAsync().WaitAsync(Timeout)).Value.Count.ShouldBe(2);
        (await second.ReceiveAsync().WaitAsync(Timeout)).Value.Count.ShouldBe(1);
        (await second.ReceiveAsync().WaitAsync(Timeout)).Value.Count.ShouldBe(2);
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
        var input = FlowMessage.Create(Json("one"));

        await node.Input.SendAsync(input);

        var snapshot = (await results.ReceiveAsync().WaitAsync(Timeout)).Value;
        snapshot.Timestamp.ShouldBe(timestamp);
        snapshot.LastObservedAt.ShouldBe(timestamp);
        snapshot.InputType.ShouldBe(typeof(JsonElement).FullName);
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
                    ["accepted"] = input.GetProperty("accepted").GetBoolean()
                }
            }));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Json(new { accepted = true })));

        (await results.ReceiveAsync().WaitAsync(Timeout)).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Logger_emits_typed_attributes_and_renders_template()
    {
        var timestamp = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await using var node = new FlowLoggerNode(
            new FlowLoggerOptions
            {
                Category = "workflow.test",
                MessageTemplate = "{kind}:{sequence}",
                AttributeSelectors = ["kind"]
            },
            new Dictionary<string, IObservabilityValueSelector<JsonElement>>(StringComparer.Ordinal)
            {
                ["kind"] = new DelegateSelector((input, _) =>
                    input.GetProperty("kind").GetString())
            },
            new FakeTimeProvider(timestamp));
        var results = Sink(node.Output);
        var input = Json(new { kind = "alpha" });

        await node.Input.SendAsync(FlowMessage.Create(input));

        var entry = (await results.ReceiveAsync().WaitAsync(Timeout)).Value;
        entry.Timestamp.ShouldBe(timestamp);
        entry.Message.ShouldBe("alpha:1");
        entry.Input.ShouldBe(input);
        entry.Attributes["kind"].ShouldBe("alpha");
    }

    [Fact]
    public async Task Logger_selector_failure_is_an_in_band_error()
    {
        await using var node = new FlowLoggerNode(
            new FlowLoggerOptions { AttributeSelectors = ["good", "broken"] },
            new Dictionary<string, IObservabilityValueSelector<JsonElement>>(StringComparer.Ordinal)
            {
                ["good"] = new DelegateSelector((_, _) => "kept"),
                ["broken"] = new DelegateSelector((_, _) =>
                    throw new InvalidOperationException("selector failed"))
            });
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Json("input")));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(ObservabilityErrorCodeNames.LoggerAttributeSelectorFailed);
        failure.Error.Details.ShouldNotBeNull().GetProperty("failedSelectors")[0].GetString()
            .ShouldBe("broken");
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Logger_does_not_expand_substituted_placeholders_and_allows_no_selectors()
    {
        await using var node = new FlowLoggerNode(new FlowLoggerOptions
        {
            Category = "workflow.test",
            MessageTemplate = "Observed {input}"
        });
        var results = Sink(node.Output);
        var events = Sink(node.Events);
        var input = FlowMessage.Create(Json("{category}"));

        await node.Input.SendAsync(input);

        var entry = (await results.ReceiveAsync().WaitAsync(Timeout)).Value;
        entry.Message.ShouldBe("Observed {category}");
        entry.Attributes.ShouldBeEmpty();
        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Name.ShouldBe(ObservabilityDiagnosticNames.LoggerEmitted);
        @event.CorrelationId.ShouldBe(input.CorrelationId);
        @event.Attributes["nodeType"].ShouldBe("log.write");
    }

    [Fact]
    public async Task Metrics_size_failure_is_in_band_and_later_input_continues()
    {
        var calls = 0;
        await using var node = new FlowMetricsNode(
            new FlowMetricsOptions { Name = "items" },
            new DelegateSelector((_, _) =>
            {
                if (++calls == 1)
                    throw new InvalidOperationException("size failed");
                return 3;
            }));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Json("first")));
        await node.Input.SendAsync(FlowMessage.Create(Json("second")));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        var success = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(ObservabilityErrorCodeNames.MetricsSizeSelectorFailed);
        success.Value.Count.ShouldBe(2);
        success.Value.TotalSize.ShouldBe(3);
    }

    [Fact]
    public async Task Metrics_non_finite_size_is_an_in_band_error()
    {
        await using var node = new FlowMetricsNode(
            new FlowMetricsOptions(),
            new DelegateSelector((_, _) => double.NaN));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Json("input")));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(ObservabilityErrorCodeNames.MetricsSizeSelectorFailed);
    }

    [Fact]
    public async Task Metrics_tracks_rate_and_averages_only_sized_observations()
    {
        var firstObservedAt = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(firstObservedAt);
        await using var node = new FlowMetricsNode(
            new FlowMetricsOptions { Name = "messages" },
            new DelegateSelector((input, _) =>
                input.GetProperty("size").ValueKind == JsonValueKind.Null
                    ? null
                    : input.GetProperty("size").GetDouble()),
            clock);
        var results = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(Json(new { size = (double?)null })));
        var first = (await results.ReceiveAsync().WaitAsync(Timeout)).Value;
        clock.Advance(TimeSpan.FromSeconds(2));
        var secondInput = FlowMessage.Create(Json(new { size = 4d }));
        await node.Input.SendAsync(secondInput);
        var second = (await results.ReceiveAsync().WaitAsync(Timeout)).Value;

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

        await node.Input.SendAsync(FlowMessage.Create(Json("one")));
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        (await results.ReceiveAsync().WaitAsync(Timeout)).IsError.ShouldBeFalse();
        (await events.ReceiveAsync().WaitAsync(Timeout)).Name
            .ShouldBe(ObservabilityDiagnosticNames.MetricsObserved);
        results.TryReceive(out _).ShouldBeFalse();
        events.TryReceive(out _).ShouldBeFalse();
    }

    private static JsonElement Json<T>(T value)
        => JsonSerializer.SerializeToElement(value);

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class DelegateSelector(
        Func<JsonElement, ObservabilityNodeContext, object?> selector)
        : IObservabilityValueSelector<JsonElement>
    {
        public object? Select(JsonElement input, ObservabilityNodeContext context)
            => selector(input, context);
    }

    private sealed class DelegateContextFactory(
        Func<JsonElement, FlowMapContext> factory)
        : IFlowMapContextFactory<JsonElement>
    {
        public FlowMapContext Create(JsonElement input) => factory(input);
    }
}
