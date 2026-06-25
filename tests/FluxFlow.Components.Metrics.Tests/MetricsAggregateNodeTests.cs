using FluxFlow.Components.Metrics;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Diagnostics;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Metrics.Tests;

// Every test news the node directly — no engine, no registry. Samples travel as
// FlowMessage<T> envelopes; the correlation id flows sample -> snapshot for free.
public sealed class MetricsAggregateNodeTests
{
    [Fact]
    public async Task Aggregate_TracksCountValueAndSize()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { RateWindowSeconds = 10 });
        var output = Sink(node.Output);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput
        {
            Timestamp = start,
            Name = "items",
            Value = 2,
            Size = 10
        }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput
        {
            Timestamp = start.AddSeconds(1),
            Name = "items",
            Value = 4,
            Size = 20
        }));
        node.Complete();

        var first = await Receive(output);
        var second = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.SampleCount.ShouldBe(1);
        first.TotalValue.ShouldBe(2);
        second.SampleCount.ShouldBe(2);
        second.ValueCount.ShouldBe(2);
        second.TotalValue.ShouldBe(6);
        second.AverageValue.ShouldBe(3);
        second.MinValue.ShouldBe(2);
        second.MaxValue.ShouldBe(4);
        second.TotalSize.ShouldBe(30);
    }

    [Fact]
    public async Task Aggregate_PreservesCorrelationIdFromSampleToSnapshot()
    {
        await using var node = new MetricsAggregateNode();
        var output = Sink(node.Output);

        var sample = FlowMessage.Create(new MetricSampleInput { Value = 1 });
        await node.Input.SendAsync(sample);
        node.Complete();

        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.CorrelationId.ShouldBe(sample.CorrelationId);   // the whole point of the envelope
        snapshot.Payload.SampleCount.ShouldBe(1);
    }

    [Fact]
    public async Task Output_FansOutEverySnapshotToEveryConsumer()
    {
        // One node's output linked to two downstream consumers, NO engine. Both see
        // every snapshot, in order.
        await using var node = new MetricsAggregateNode();
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 1 }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 2 }));
        node.Complete();

        (await Receive(logger)).SampleCount.ShouldBe(1);
        (await Receive(logger)).SampleCount.ShouldBe(2);
        (await Receive(mapper)).SampleCount.ShouldBe(1);
        (await Receive(mapper)).SampleCount.ShouldBe(2);
    }

    [Fact]
    public async Task Aggregate_CalculatesRatesFromSampleTimestamps()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { RateWindowSeconds = 2 });
        var output = Sink(node.Output);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Timestamp = start }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Timestamp = start.AddSeconds(1) }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Timestamp = start.AddSeconds(3) }));
        node.Complete();

        await Receive(output);
        await Receive(output);
        var third = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        third.SampleCount.ShouldBe(3);
        third.CurrentRate.ShouldBe(1d);
        third.AverageRate.ShouldBe(1d);
    }

    [Fact]
    public async Task Aggregate_GroupsByTag()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions
        {
            GroupByTag = "topic",
            RateWindowSeconds = 10
        });
        var output = Sink(node.Output);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput
        {
            Timestamp = start,
            Group = "ignored",
            Size = 3,
            Tags = new Dictionary<string, string> { ["topic"] = "sensors/a" }
        }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput
        {
            Timestamp = start.AddSeconds(1),
            Group = "ignored",
            Size = 4,
            Tags = new Dictionary<string, string> { ["topic"] = "sensors/b" }
        }));
        node.Complete();

        await Receive(output);
        var second = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        second.Groups.Keys.ShouldBe(["sensors/a", "sensors/b"], ignoreOrder: true);
        second.Groups["sensors/a"].Count.ShouldBe(1);
        second.Groups["sensors/a"].TotalSize.ShouldBe(3);
        second.Groups["sensors/b"].Count.ShouldBe(1);
        second.Groups["sensors/b"].TotalSize.ShouldBe(4);
    }

    [Fact]
    public async Task Aggregate_TrimsInactiveGroupRatesFromSnapshotTimestamp()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { RateWindowSeconds = 2 });
        var output = Sink(node.Output);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Timestamp = start, Group = "a" }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Timestamp = start.AddSeconds(3), Group = "b" }));
        node.Complete();

        await Receive(output);
        var second = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        second.Groups["a"].CurrentRate.ShouldBe(0);
        second.Groups["b"].CurrentRate.ShouldBe(0.5);
        second.CurrentRate.ShouldBe(0.5);
    }

    [Fact]
    public async Task Aggregate_HandlesNullTagsAsDefaultGroup()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { GroupByTag = "topic" });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Tags = null! }));
        node.Complete();

        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.Groups.Keys.ShouldBe(["default"]);
    }

    [Fact]
    public async Task Aggregate_EmitsOnlyFinalSnapshotWhenConfigured()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { EmitEverySample = false });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 1 }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 2 }));
        node.Complete();

        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.SampleCount.ShouldBe(2);
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Aggregate_MissingValueCountsAsEventWithoutNumericZero()
    {
        await using var node = new MetricsAggregateNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput()));
        node.Complete();
        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.SampleCount.ShouldBe(1);
        snapshot.ValueCount.ShouldBe(0);
        snapshot.TotalValue.ShouldBeNull();
        snapshot.AverageValue.ShouldBeNull();
    }

    [Fact]
    public async Task Aggregate_CanTreatMissingValueAsZero()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { TreatMissingValueAsZero = true });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput()));
        node.Complete();
        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.SampleCount.ShouldBe(1);
        snapshot.ValueCount.ShouldBe(1);
        snapshot.TotalValue.ShouldBe(0);
        snapshot.AverageValue.ShouldBe(0);
    }

    [Fact]
    public async Task Aggregate_UsesConfiguredClockForMissingSampleTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:42Z");
        var timeProvider = new FakeTimeProvider(timestamp);
        await using var node = new MetricsAggregateNode(clock: timeProvider);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput()));
        node.Complete();
        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.Timestamp.ShouldBe(timestamp);
        snapshot.Latest.ShouldNotBeNull();
        snapshot.Latest.Timestamp.ShouldBe(timestamp);
        snapshot.Groups["default"].LatestTimestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Aggregate_RespectsMaxGroupLimit()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions { MaxGroups = 1 });
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var rejected = FlowMessage.Create(new MetricSampleInput { Group = "b" });
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Group = "a" }));
        await node.Input.SendAsync(rejected);
        node.Complete();

        await Receive(output);
        var second = await Receive(output);
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        second.SampleCount.ShouldBe(2);
        second.Groups.Keys.ShouldBe(["a"]);
        error.Code.ShouldBe(MetricsErrorCodes.GroupLimitReached);
        error.CorrelationId.ShouldBe(rejected.CorrelationId);
    }

    [Fact]
    public async Task Aggregate_KeepsGlobalTotalsForRejectedGroupSamples()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions
        {
            MaxGroups = 1,
            EmitEverySample = false
        });
        var output = Sink(node.Output);
        Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Group = "a", Value = 1 }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Group = "b", Value = 2 }));
        node.Complete();

        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.SampleCount.ShouldBe(2);
        snapshot.ValueCount.ShouldBe(2);
        snapshot.TotalValue.ShouldBe(3);
        snapshot.Groups.Keys.ShouldBe(["a"]);
    }

    [Fact]
    public async Task Aggregate_CapsRejectedGroupTrackingWithSummaryError()
    {
        await using var node = new MetricsAggregateNode(new MetricsAggregateOptions
        {
            MaxGroups = 1,
            EmitEverySample = false
        });
        Sink(node.Output);
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Group = "tracked" }));
        for (var index = 0; index < 1030; index++)
        {
            await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Group = $"rejected-{index}" }));
        }

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var received = new List<FlowError>();
        while (await errors.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            while (errors.TryReceive(out var error))
            {
                received.Add(error);
            }
        }

        received.ShouldAllBe(error => error.Code == MetricsErrorCodes.GroupLimitReached);
        received.Count.ShouldBe(1025);
        received[^1].Message.ShouldContain("not itemized");
    }

    [Fact]
    public async Task Aggregate_ReportsInvalidSizeAndContinues()
    {
        await using var node = new MetricsAggregateNode();
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new MetricSampleInput { Size = -1 });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Size = 3 }));
        node.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(MetricsErrorCodes.InvalidSample);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        snapshot.SampleCount.ShouldBe(1);
        snapshot.TotalSize.ShouldBe(3);
    }

    [Fact]
    public async Task Aggregate_CompletesCleanlyWithUnlinkedOutput()
    {
        await using var node = new MetricsAggregateNode();

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 1, Size = 10 }));
        node.Complete();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Aggregate_DeliversEverySnapshotInOrderToASlowConsumer()
    {
        // A consumer that drains one snapshot at a time still receives every
        // snapshot, in order — the single-DOP pump posts each broadcast before the
        // next sample is processed.
        await using var node = new MetricsAggregateNode();
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 1 }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 2 }));
        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 3 }));
        node.Complete();

        var first = await Receive(output);
        var second = await Receive(output);
        var third = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.SampleCount.ShouldBe(1);
        second.SampleCount.ShouldBe(2);
        third.SampleCount.ShouldBe(3);
        errors.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Aggregate_PausedConsumerReceivesEverySnapshotWithoutDropping()
    {
        const int sampleCount = 8;
        await using var node = new MetricsAggregateNode();
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        for (var index = 0; index < sampleCount; index++)
        {
            await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = index }));
        }

        node.Complete();

        var counts = new List<long>();
        for (var index = 0; index < sampleCount; index++)
        {
            counts.Add((await Receive(output)).SampleCount);
        }

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        counts.ShouldBe(Enumerable.Range(1, sampleCount).Select(value => (long)value));
        output.TryReceive(out _).ShouldBeFalse();
        errors.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Aggregate_EmitsEventsCarryingCorrelationId()
    {
        await using var node = new MetricsAggregateNode();
        Sink(node.Output);
        var events = Sink(node.Events);

        var sample = FlowMessage.Create(new MetricSampleInput { Value = 1, Group = "items" });
        await node.Input.SendAsync(sample);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(MetricsDiagnosticNames.AggregateUpdated);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(sample.CorrelationId);
        @event.Attributes["sampleCount"].ShouldBe(1L);
    }

    [Fact]
    public void Aggregate_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new MetricsAggregateNode(new MetricsAggregateOptions { BoundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Aggregate_RejectsInvalidRateWindow(double rateWindowSeconds)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new MetricsAggregateNode(new MetricsAggregateOptions
            {
                RateWindowSeconds = rateWindowSeconds
            }));

        exception.Message.ShouldContain("rateWindowSeconds");
    }

    [Fact]
    public void Aggregate_RejectsInvalidMaxGroups()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new MetricsAggregateNode(new MetricsAggregateOptions { MaxGroups = -1 }));

        exception.Message.ShouldContain("maxGroups");
    }

    [Fact]
    public async Task Aggregate_UsesDefaultsWhenConstructedWithoutArguments()
    {
        await using var node = new MetricsAggregateNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 5 }));
        node.Complete();

        var snapshot = await Receive(output);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.SampleCount.ShouldBe(1);
        snapshot.TotalValue.ShouldBe(5);
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static async Task<MetricSnapshotOutput> Receive(BufferBlock<FlowMessage<MetricSnapshotOutput>> output)
        => (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
}
