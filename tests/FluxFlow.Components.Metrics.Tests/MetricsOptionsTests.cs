using FluxFlow.Components.Metrics.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Metrics.Tests;

public sealed class MetricsOptionsTests
{
    [Fact]
    public void Aggregate_options_normalize_group_tag()
    {
        var options = new MetricsAggregateOptions
        {
            RateWindowSeconds = 10,
            BoundedCapacity = 4,
            MaxGroups = 2,
            GroupByTag = " topic "
        };

        options.RateWindowSeconds.ShouldBe(10);
        options.BoundedCapacity.ShouldBe(4);
        options.MaxGroups.ShouldBe(2);
        options.GroupByTag.ShouldBe("topic");
    }

    [Fact]
    public void Aggregate_options_treat_blank_group_tag_as_absent()
    {
        var options = new MetricsAggregateOptions { GroupByTag = " " };

        options.GroupByTag.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Aggregate_options_reject_invalid_rate_window(double value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new MetricsAggregateOptions { RateWindowSeconds = value });

        exception.Message.ShouldContain("rateWindowSeconds");
    }

    [Fact]
    public void Aggregate_options_reject_invalid_capacity_and_group_limit()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new MetricsAggregateOptions { BoundedCapacity = 0 })
            .Message.ShouldContain("boundedCapacity");
        Should.Throw<ArgumentOutOfRangeException>(
            () => new MetricsAggregateOptions { MaxGroups = -1 })
            .Message.ShouldContain("maxGroups");
    }
}
