using FluxFlow.Engine.Benchmarks.LoadQualification;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests.LoadQualification;

public sealed class LatencyHistogramTests
{
    [Theory]
    [InlineData(.50, 10d)]
    [InlineData(.95, 100d)]
    public void Histogram_uses_nearest_rank_for_fractional_sample_positions(double quantile, double expected)
    {
        var histogram = new LatencyHistogram();
        foreach (var value in new[] { 1d, 10d, 100d }) histogram.Record(value);
        histogram.UpperBound(quantile).ShouldBeInRange(expected,
            (expected + .001) * Math.Pow(2, 1d / 64) - .001 + 1e-9);
        histogram.Count.ShouldBe(3);
    }

    [Fact]
    public void Histogram_reports_bounded_order_statistics_from_individual_samples()
    {
        var histogram = new LatencyHistogram();
        histogram.Summarize().ShouldBe(new LatencySummary(0, 0, 0, 0));
        for (var index = 0; index < 100; index++) histogram.Record(index);
        histogram.Count.ShouldBe(100);
        foreach (var (quantile, expected) in new[] { (.50, 49d), (.95, 94d), (.99, 98d), (1d, 99d) })
            histogram.UpperBound(quantile).ShouldBeInRange(expected,
                (expected + .001) * Math.Pow(2, 1d / 64) - .001 + 1e-9);
        histogram.Summarize().Samples.ShouldBe(100);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void Histogram_rejects_invalid_observations_without_changing_counts(double value)
    {
        var histogram = new LatencyHistogram();
        Should.Throw<ArgumentOutOfRangeException>(() => histogram.Record(value));
        histogram.Count.ShouldBe(0);
        histogram.Record(0);
        histogram.Count.ShouldBe(1);
        histogram.UpperBound(1).ShouldBeInRange(0, .000011);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1.1d)]
    [InlineData(double.NaN)]
    public void Histogram_rejects_invalid_quantiles_even_when_empty(double value)
    {
        var histogram = new LatencyHistogram();
        Should.Throw<ArgumentOutOfRangeException>(() => histogram.UpperBound(value));
        histogram.Count.ShouldBe(0);
    }

    [Fact]
    public void Concurrent_histogram_writers_preserve_every_sample()
    {
        var histogram = new LatencyHistogram();
        Parallel.For(0, 10_000, index => histogram.Record(index % 100));
        histogram.Count.ShouldBe(10_000);
        histogram.UpperBound(.95).ShouldBeInRange(94, 95.1);
    }
}
