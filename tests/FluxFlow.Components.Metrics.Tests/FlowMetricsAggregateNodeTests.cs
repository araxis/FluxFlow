using System.Numerics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Metrics.Tests;

public sealed class FlowMetricsAggregateNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Samples_emit_ordered_snapshots_with_groups_rates_and_lineage()
    {
        var start = DateTimeOffset.Parse("2026-07-19T17:00:00Z");
        await using var node = new FlowMetricsAggregateNode(new MetricsAggregateOptions
        {
            RateWindowSeconds = 10,
            GroupByTag = "topic"
        });
        var results = Link(node.Output);
        var first = FlowMessage.Create(new MetricSampleInput
        {
            Timestamp = start,
            Name = "messages",
            Value = 2,
            Size = 10,
            Tags = new Dictionary<string, string> { ["topic"] = "a" }
        }) with
        {
            Headers = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
            {
                ["tenant"] = FlowValue.From("north")
            }
        };
        var second = FlowMessage.Create(new MetricSampleInput
        {
            Timestamp = start.AddSeconds(1),
            Name = "messages",
            Value = 4,
            Size = 20,
            Tags = new Dictionary<string, string> { ["topic"] = "b" }
        });

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);

        var firstResult = await results.ReceiveAsync().WaitAsync(Timeout);
        var secondResult = await results.ReceiveAsync().WaitAsync(Timeout);

        firstResult.Payload.Kind.ShouldBe(MetricsResultKinds.Snapshot);
        firstResult.TraceId.ShouldBe(first.TraceId);
        firstResult.CausationId.ShouldBe(first.MessageId);
        firstResult.Headers.ShouldBeSameAs(first.Headers);
        firstResult.Payload.Value.ShouldNotBeNull().SampleCount.ShouldBe(1);

        var snapshot = secondResult.Payload.Value.ShouldNotBeNull();
        snapshot.SampleCount.ShouldBe(2);
        snapshot.ValueCount.ShouldBe(2);
        snapshot.TotalValue.ShouldBe(6);
        snapshot.AverageValue.ShouldBe(3);
        snapshot.TotalSize.ShouldBe(30);
        snapshot.CurrentRate.ShouldBe(0.2d);
        snapshot.Groups.Keys.ShouldBe(["a", "b"], ignoreOrder: true);
    }

    [Fact]
    public async Task Invalid_sample_is_normal_failure_and_later_sample_continues()
    {
        await using var node = new FlowMetricsAggregateNode();
        var results = Link(node.Output);
        var bad = FlowMessage.Create(
            new MetricSampleInput { Size = -1 },
            new CorrelationId("bad"));
        var good = FlowMessage.Create(
            new MetricSampleInput { Size = 3 },
            new CorrelationId("good"));

        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(good);

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        var success = await results.ReceiveAsync().WaitAsync(Timeout);

        failure.CorrelationId.ShouldBe(bad.CorrelationId);
        failure.Payload.Kind.ShouldBe(MetricsResultKinds.AggregateFailed);
        failure.Payload.IsError.ShouldBeTrue();
        failure.Payload.Value.ShouldBeNull();
        failure.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(MetricsErrorCodeNames.InvalidSample);
        failure.Payload.Error.Details.GetObject()["legacyCode"].GetInteger()
            .ShouldBe(new BigInteger(MetricsErrorCodes.InvalidSample));
        success.CorrelationId.ShouldBe(good.CorrelationId);
        success.Payload.Value.ShouldNotBeNull().TotalSize.ShouldBe(3);
    }

    [Fact]
    public async Task Group_limit_failure_carries_updated_global_snapshot()
    {
        await using var node = new FlowMetricsAggregateNode(new MetricsAggregateOptions
        {
            MaxGroups = 1
        });
        var results = Link(node.Output);
        var rejected = FlowMessage.Create(
            new MetricSampleInput { Group = "b", Value = 2 },
            new CorrelationId("rejected"));

        await node.Input.SendAsync(FlowMessage.Create(
            new MetricSampleInput { Group = "a", Value = 1 }));
        await node.Input.SendAsync(rejected);

        await results.ReceiveAsync().WaitAsync(Timeout);
        var partial = await results.ReceiveAsync().WaitAsync(Timeout);

        partial.CorrelationId.ShouldBe(rejected.CorrelationId);
        partial.Payload.Kind.ShouldBe(MetricsResultKinds.GroupLimitReached);
        partial.Payload.IsError.ShouldBeTrue();
        partial.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(MetricsErrorCodeNames.GroupLimitReached);
        var snapshot = partial.Payload.Value.ShouldNotBeNull();
        snapshot.SampleCount.ShouldBe(2);
        snapshot.TotalValue.ShouldBe(3);
        snapshot.Groups.Keys.ShouldBe(["a"]);
    }

    [Fact]
    public async Task Every_sample_for_an_untracked_group_is_reported_as_partial()
    {
        await using var node = new FlowMetricsAggregateNode(new MetricsAggregateOptions
        {
            MaxGroups = 1
        });
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(
            new MetricSampleInput { Group = "a", Value = 1 }));
        await node.Input.SendAsync(FlowMessage.Create(
            new MetricSampleInput { Group = "b", Value = 2 }));
        await node.Input.SendAsync(FlowMessage.Create(
            new MetricSampleInput { Group = "b", Value = 3 }));

        await results.ReceiveAsync().WaitAsync(Timeout);
        var firstPartial = await results.ReceiveAsync().WaitAsync(Timeout);
        var secondPartial = await results.ReceiveAsync().WaitAsync(Timeout);

        firstPartial.Payload.Kind.ShouldBe(MetricsResultKinds.GroupLimitReached);
        secondPartial.Payload.Kind.ShouldBe(MetricsResultKinds.GroupLimitReached);
        secondPartial.Payload.Value.ShouldNotBeNull().SampleCount.ShouldBe(3);
        secondPartial.Payload.Value.TotalValue.ShouldBe(6);
        secondPartial.Payload.Value.Groups.Keys.ShouldBe(["a"]);
    }

    [Fact]
    public async Task Coalesced_mode_emits_group_failure_then_one_final_snapshot()
    {
        await using var node = new FlowMetricsAggregateNode(new MetricsAggregateOptions
        {
            EmitEverySample = false,
            MaxGroups = 1
        });
        var results = Link(node.Output);
        var first = FlowMessage.Create(new MetricSampleInput { Group = "a", Value = 1 });
        var last = FlowMessage.Create(
            new MetricSampleInput { Group = "b", Value = 2 },
            new CorrelationId("last"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(last);
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        var partial = await results.ReceiveAsync().WaitAsync(Timeout);
        var final = await results.ReceiveAsync().WaitAsync(Timeout);
        partial.Payload.Kind.ShouldBe(MetricsResultKinds.GroupLimitReached);
        final.Payload.Kind.ShouldBe(MetricsResultKinds.FinalSnapshot);
        final.CorrelationId.ShouldBe(last.CorrelationId);
        final.TraceId.ShouldBe(last.TraceId);
        final.CausationId.ShouldBe(last.MessageId);
        final.Payload.Value.ShouldNotBeNull().TotalValue.ShouldBe(3);
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_timestamp_uses_clock_and_coalesced_completion_is_exact_once()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-19T17:05:00Z");
        var clock = new FakeTimeProvider(timestamp);
        await using var node = new FlowMetricsAggregateNode(
            new MetricsAggregateOptions { EmitEverySample = false },
            clock);
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 1 }));
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        var result = await results.ReceiveAsync().WaitAsync(Timeout);
        result.Payload.Kind.ShouldBe(MetricsResultKinds.FinalSnapshot);
        var snapshot = result.Payload.Value.ShouldNotBeNull();
        snapshot.Timestamp.ShouldBe(timestamp);
        snapshot.Latest.ShouldNotBeNull().Timestamp.ShouldBe(timestamp);
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task Non_finite_values_are_normal_invalid_sample_results(double value)
    {
        await using var node = new FlowMetricsAggregateNode();
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = value }));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.Payload.IsError.ShouldBeTrue();
        failure.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(MetricsErrorCodeNames.InvalidSample);
    }

    [Fact]
    public async Task Missing_sample_is_normal_failure()
    {
        await using var node = new FlowMetricsAggregateNode();
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create<MetricSampleInput>(null!));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.Payload.IsError.ShouldBeTrue();
        failure.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(MetricsErrorCodeNames.InvalidSample);
    }

    [Fact]
    public async Task Output_fans_out_each_result_to_every_consumer()
    {
        await using var node = new FlowMetricsAggregateNode();
        var first = Link(node.Output);
        var second = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 1 }));

        (await first.ReceiveAsync().WaitAsync(Timeout)).Payload.Value
            .ShouldNotBeNull().SampleCount.ShouldBe(1);
        (await second.ReceiveAsync().WaitAsync(Timeout)).Payload.Value
            .ShouldNotBeNull().SampleCount.ShouldBe(1);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }
}
