using FluxFlow.Components.Metrics.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Metrics.Tests;

public sealed class MetricsContractTests
{
    [Fact]
    public void Sample_input_normalizes_text_and_copies_tags()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["topic"] = "orders"
        };

        var sample = new MetricSampleInput
        {
            Timestamp = new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero),
            Name = " messages ",
            Group = " group-a ",
            Unit = " items ",
            Tags = tags
        };
        tags["topic"] = "changed";
        tags["new"] = "value";

        sample.Name.ShouldBe("messages");
        sample.Group.ShouldBe("group-a");
        sample.Unit.ShouldBe("items");
        sample.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        sample.Tags["topic"].ShouldBe("orders");
        sample.Tags.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Snapshot_output_normalizes_text_and_deep_copies_latest_and_groups()
    {
        var latestTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["topic"] = "orders"
        };
        var latest = new MetricSampleInput
        {
            Name = " sample ",
            Tags = latestTags
        };
        var groups = new Dictionary<string, MetricGroupSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = new()
            {
                Group = " orders ",
                Count = 1,
                CurrentRate = 1,
                LatestTimestamp = DateTimeOffset.UnixEpoch
            }
        };

        var snapshot = new MetricSnapshotOutput
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Name = " messages ",
            Unit = " items ",
            Latest = latest,
            Groups = groups
        };
        latestTags["topic"] = "changed";
        latestTags["new"] = "value";
        groups["orders"] = groups["orders"] with { Count = 2 };
        groups["new"] = groups["orders"];

        snapshot.Name.ShouldBe("messages");
        snapshot.Unit.ShouldBe("items");
        snapshot.Latest.ShouldNotBeNull().Name.ShouldBe("sample");
        snapshot.Latest.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        snapshot.Latest.Tags["topic"].ShouldBe("orders");
        snapshot.Latest.Tags.ContainsKey("new").ShouldBeFalse();
        snapshot.Groups.Comparer.ShouldBe(StringComparer.Ordinal);
        snapshot.Groups["orders"].Group.ShouldBe("orders");
        snapshot.Groups["orders"].Count.ShouldBe(1);
        snapshot.Groups.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Contracts_treat_blank_text_and_null_maps_as_absent()
    {
        var sample = new MetricSampleInput
        {
            Name = " ",
            Group = "\t",
            Unit = "\r\n",
            Tags = null!
        };
        var snapshot = new MetricSnapshotOutput
        {
            Name = " ",
            Unit = "\t",
            Latest = null,
            Groups = null!
        };
        var group = new MetricGroupSnapshot
        {
            Group = null!,
            Count = 0,
            CurrentRate = 0,
            LatestTimestamp = DateTimeOffset.UnixEpoch
        };

        sample.Name.ShouldBeNull();
        sample.Group.ShouldBeNull();
        sample.Unit.ShouldBeNull();
        sample.Tags.ShouldBeEmpty();
        sample.Tags.Comparer.ShouldBe(StringComparer.Ordinal);
        snapshot.Name.ShouldBeNull();
        snapshot.Unit.ShouldBeNull();
        snapshot.Latest.ShouldBeNull();
        snapshot.Groups.ShouldBeEmpty();
        snapshot.Groups.Comparer.ShouldBe(StringComparer.Ordinal);
        group.Group.ShouldBeEmpty();
    }
}
