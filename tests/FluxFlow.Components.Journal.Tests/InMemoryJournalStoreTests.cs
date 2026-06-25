using FluxFlow.Components.Journal.Contracts;
using FluxFlow.Components.Journal.Stores;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Journal.Tests;

public sealed class InMemoryJournalStoreTests
{
    [Fact]
    public async Task QueryAsync_filters_records_by_event_fields_and_attributes()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord(
            "1",
            type: "order.created",
            status: "ok",
            source: "worker",
            workflowId: "wf-1",
            workflowName: "orders",
            nodeId: "node-1",
            componentId: "component-1",
            subject: "orders/42",
            channel: "events/orders",
            severity: "info",
            level: "low",
            attributes: new Dictionary<string, string> { ["tenant"] = "primary" }));
        await store.AppendAsync(CreateRecord(
            "2",
            type: "order.failed",
            status: "failed",
            source: "worker",
            workflowId: "wf-1",
            workflowName: "orders",
            nodeId: "node-2",
            componentId: "component-2",
            subject: "orders/42",
            channel: "events/orders",
            severity: "error",
            level: "high",
            attributes: new Dictionary<string, string> { ["tenant"] = "secondary" }));

        var result = await store.QueryAsync(new JournalQuery
        {
            TypePrefix = "order.",
            Status = "ok",
            Source = "worker",
            WorkflowId = "wf-1",
            WorkflowName = "orders",
            NodeId = "node-1",
            ComponentId = "component-1",
            SubjectPrefix = "orders/",
            ChannelPrefix = "events/",
            Severity = "info",
            Level = "low",
            Attributes = new Dictionary<string, string> { ["tenant"] = "primary" }
        });

        result.TotalMatched.ShouldBe(1);
        result.Records.Single().Id.ShouldBe("1");
        result.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task QueryAsync_excludes_subject_and_channel_prefixes()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("1", subject: "jobs/private/1", channel: "internal/jobs"));
        await store.AppendAsync(CreateRecord("2", subject: "jobs/public/2", channel: "public/jobs"));

        var result = await store.QueryAsync(new JournalQuery
        {
            SubjectPrefix = "jobs/",
            ChannelPrefix = "public/",
            ExcludedSubjectPrefix = "jobs/private/",
            ExcludedChannelPrefix = "internal/"
        });

        result.Records.Select(record => record.Id).ShouldBe(["2"]);
    }

    [Fact]
    public async Task QueryAsync_applies_time_range_and_pagination()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));
        await store.AppendAsync(CreateRecord("3", timestamp: Timestamp(2)));
        await store.AppendAsync(CreateRecord("4", timestamp: Timestamp(3)));

        var result = await store.QueryAsync(new JournalQuery
        {
            From = Timestamp(1),
            To = Timestamp(3),
            Offset = 1,
            Limit = 1
        });

        result.TotalMatched.ShouldBe(3);
        result.Records.Select(record => record.Id).ShouldBe(["3"]);
        result.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task PruneAsync_removes_old_records_and_keeps_latest_max_records()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));
        await store.AppendAsync(CreateRecord("3", timestamp: Timestamp(2)));
        await store.AppendAsync(CreateRecord("4", timestamp: Timestamp(3)));

        var prune = await store.PruneAsync(new JournalRetentionOptions
        {
            DeleteBefore = Timestamp(1),
            MaxRecords = 2
        });
        var result = await store.QueryAsync(new JournalQuery());

        prune.Removed.ShouldBe(2);
        prune.Remaining.ShouldBe(2);
        result.Records.Select(record => record.Id).ShouldBe(["3", "4"]);
    }

    [Fact]
    public async Task PruneAsync_uses_reference_time_for_age_based_retention()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("old", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("new", timestamp: Timestamp(5)));

        await store.PruneAsync(new JournalRetentionOptions
        {
            MaxAge = TimeSpan.FromMinutes(2),
            ReferenceTime = Timestamp(6)
        });
        var result = await store.QueryAsync(new JournalQuery());

        result.Records.Select(record => record.Id).ShouldBe(["new"]);
    }

    [Fact]
    public async Task AppendAsync_applies_max_records_retention_on_append()
    {
        var store = new InMemoryJournalStore(new JournalRetentionOptions
        {
            MaxRecords = 2
        });

        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));
        await store.AppendAsync(CreateRecord("3", timestamp: Timestamp(2)));
        var result = await store.QueryAsync(new JournalQuery());

        result.Records.Select(record => record.Id).ShouldBe(["2", "3"]);
    }

    [Fact]
    public void Constructor_rejects_negative_max_records_retention()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new InMemoryJournalStore(new JournalRetentionOptions
            {
                MaxRecords = -1
            }));
    }

    [Fact]
    public async Task AppendAsync_detects_duplicates_after_retention_and_prune()
    {
        var store = new InMemoryJournalStore(new JournalRetentionOptions
        {
            MaxRecords = 2
        });
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));

        await store.PruneAsync(new JournalRetentionOptions { MaxRecords = 1 });

        // "1" was pruned, so its id can be appended again; "2" still exists.
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(2)));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.AppendAsync(CreateRecord("2", timestamp: Timestamp(3))).AsTask());

        var result = await store.QueryAsync(new JournalQuery());
        result.Records.Select(record => record.Id).ShouldBe(["2", "1"]);
    }

    [Fact]
    public async Task AppendAsync_rejects_duplicate_record_ids()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("same"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.AppendAsync(CreateRecord("same")).AsTask());
    }

    [Fact]
    public async Task AppendAsync_normalizes_record_fields_and_attributes()
    {
        var store = new InMemoryJournalStore();

        var appended = await store.AppendAsync(CreateRecord(
            "  spaced-id  ",
            type: " item.accepted ",
            status: " ok ",
            source: " source-a ",
            workflowId: " wf-1 ",
            workflowName: " main ",
            nodeId: " node-1 ",
            componentId: " component-1 ",
            subject: " items/1 ",
            channel: " events/items ",
            severity: " info ",
            level: " low ",
            attributes: new Dictionary<string, string>
            {
                [" tenant "] = " primary "
            }));

        appended.Record.Id.ShouldBe("spaced-id");
        appended.Record.Type.ShouldBe("item.accepted");
        appended.Record.Status.ShouldBe("ok");
        appended.Record.Source.ShouldBe("source-a");
        appended.Record.WorkflowId.ShouldBe("wf-1");
        appended.Record.WorkflowName.ShouldBe("main");
        appended.Record.NodeId.ShouldBe("node-1");
        appended.Record.ComponentId.ShouldBe("component-1");
        appended.Record.Subject.ShouldBe("items/1");
        appended.Record.Channel.ShouldBe("events/items");
        appended.Record.Severity.ShouldBe("info");
        appended.Record.Level.ShouldBe("low");
        appended.Record.Attributes.ContainsKey("tenant").ShouldBeTrue();
        appended.Record.Attributes["tenant"].ShouldBe("primary");
    }

    [Fact]
    public async Task AppendAsync_rejects_blank_attribute_values()
    {
        var store = new InMemoryJournalStore();

        await Should.ThrowAsync<ArgumentException>(() =>
            store.AppendAsync(CreateRecord(
                "blank-attribute",
                attributes: new Dictionary<string, string>
                {
                    ["tenant"] = " "
                })).AsTask());
    }

    [Fact]
    public async Task AppendAsync_rejects_duplicate_attribute_keys_after_trimming()
    {
        var store = new InMemoryJournalStore();

        await Should.ThrowAsync<ArgumentException>(() =>
            store.AppendAsync(CreateRecord(
                "duplicate-attributes",
                attributes: new Dictionary<string, string>
                {
                    [" tenant "] = "primary",
                    ["tenant"] = "secondary"
                })).AsTask());
    }

    [Fact]
    public async Task QueryAsync_validates_pagination()
    {
        var store = new InMemoryJournalStore();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            store.QueryAsync(new JournalQuery { Limit = 0 }).AsTask());
    }

    [Fact]
    public async Task PruneAsync_requires_reference_time_for_max_age()
    {
        var store = new InMemoryJournalStore();

        await Should.ThrowAsync<ArgumentException>(() =>
            store.PruneAsync(new JournalRetentionOptions
            {
                MaxAge = TimeSpan.FromMinutes(1)
            }).AsTask());
    }

    [Fact]
    public void FromEvent_maps_neutral_event_fields()
    {
        var input = new JournalEventInput
        {
            Timestamp = Timestamp(1),
            Type = "item.accepted",
            Source = "source-a",
            SourceNodeId = "node-1",
            Subject = "items/1",
            Status = "ok",
            Channel = "items",
            PayloadBytes = 12,
            PayloadPreview = "preview",
            Attributes = new Dictionary<string, string>
            {
                [JournalRecordMapper.WorkflowIdAttribute] = "wf-1",
                [JournalRecordMapper.WorkflowNameAttribute] = "main",
                [JournalRecordMapper.ComponentIdAttribute] = "component-1",
                [JournalRecordMapper.SeverityAttribute] = "info",
                [JournalRecordMapper.LevelAttribute] = "low",
                [JournalRecordMapper.SummaryAttribute] = "accepted"
            }
        };

        var record = JournalRecordMapper.FromEvent(input, "evt-1");

        record.Id.ShouldBe("evt-1");
        record.Timestamp.ShouldBe(input.Timestamp);
        record.Type.ShouldBe(input.Type);
        record.Source.ShouldBe(input.Source);
        record.NodeId.ShouldBe("node-1");
        record.ComponentId.ShouldBe("component-1");
        record.WorkflowId.ShouldBe("wf-1");
        record.WorkflowName.ShouldBe("main");
        record.Severity.ShouldBe("info");
        record.Level.ShouldBe("low");
        record.Summary.ShouldBe("accepted");
        record.PayloadBytes.ShouldBe(12);
        record.PayloadPreview.ShouldBe("preview");
    }

    [Fact]
    public void FromEvent_uses_node_attribute_when_source_node_id_is_missing()
    {
        var input = new JournalEventInput
        {
            Timestamp = Timestamp(1),
            Type = "item.accepted",
            Attributes = new Dictionary<string, string>
            {
                [JournalRecordMapper.NodeIdAttribute] = "node-from-attributes"
            }
        };

        var record = JournalRecordMapper.FromEvent(input, "evt-1");

        record.NodeId.ShouldBe("node-from-attributes");
    }

    [Fact]
    public void FromEvent_normalizes_event_fields_and_attributes()
    {
        var input = new JournalEventInput
        {
            Timestamp = Timestamp(1),
            Type = " item.accepted ",
            Source = " source-a ",
            SourceNodeId = " node-1 ",
            Subject = " items/1 ",
            Status = " ok ",
            Channel = " events/items ",
            PayloadPreview = " accepted ",
            Attributes = new Dictionary<string, string>
            {
                [" tenant "] = " primary ",
                [JournalRecordMapper.WorkflowIdAttribute] = " wf-1 ",
                [JournalRecordMapper.SummaryAttribute] = " summary "
            }
        };

        var record = JournalRecordMapper.FromEvent(input, " evt-1 ");

        record.Id.ShouldBe("evt-1");
        record.Type.ShouldBe("item.accepted");
        record.Source.ShouldBe("source-a");
        record.NodeId.ShouldBe("node-1");
        record.Subject.ShouldBe("items/1");
        record.Status.ShouldBe("ok");
        record.Channel.ShouldBe("events/items");
        record.WorkflowId.ShouldBe("wf-1");
        record.Summary.ShouldBe("summary");
        record.PayloadPreview.ShouldBe("accepted");
        record.Attributes["tenant"].ShouldBe("primary");
    }

    [Fact]
    public void FromEvent_rejects_duplicate_attribute_keys_after_trimming()
    {
        var input = new JournalEventInput
        {
            Timestamp = Timestamp(1),
            Attributes = new Dictionary<string, string>
            {
                [" tenant "] = "primary",
                ["tenant"] = "secondary"
            }
        };

        Should.Throw<ArgumentException>(() =>
            JournalRecordMapper.FromEvent(input, "evt-1"));
    }

    [Fact]
    public void FromEvent_rejects_blank_record_id()
    {
        Should.Throw<ArgumentException>(() =>
            JournalRecordMapper.FromEvent(new JournalEventInput { Timestamp = Timestamp(1) }, " "));
    }

    private static JournalRecord CreateRecord(
        string id,
        DateTimeOffset? timestamp = null,
        string type = "item.accepted",
        string status = "ok",
        string source = "source-a",
        string? workflowId = null,
        string? workflowName = null,
        string? nodeId = null,
        string? componentId = null,
        string? subject = null,
        string? channel = null,
        string? severity = null,
        string? level = null,
        Dictionary<string, string>? attributes = null)
        => new()
        {
            Id = id,
            Timestamp = timestamp ?? Timestamp(0),
            Type = type,
            Status = status,
            Source = source,
            WorkflowId = workflowId,
            WorkflowName = workflowName,
            NodeId = nodeId,
            ComponentId = componentId,
            Subject = subject,
            Channel = channel,
            Severity = severity,
            Level = level,
            Attributes = attributes ?? []
        };

    private static DateTimeOffset Timestamp(int minute)
        => new(2026, 1, 1, 0, minute, 0, TimeSpan.Zero);
}
