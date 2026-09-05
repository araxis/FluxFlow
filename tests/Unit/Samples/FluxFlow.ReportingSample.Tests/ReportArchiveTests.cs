using System.Globalization;
using System.Text.Json;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.ReportingSample.Tests;

[Trait("Category", "Integration")]
public sealed class ReportArchiveTests : IDisposable
{
    private static readonly DateTimeOffset EventTime = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("report-archive-tests-").FullName;
    private readonly Guid _streamId = Guid.NewGuid();

    [Fact]
    public async Task Reopened_archive_preserves_aggregation_and_idempotent_replay()
    {
        var archive = new ReportArchive(_directory);
        var first = Event(1, "order.accepted");
        (await archive.AppendAsync(first)).ShouldBeTrue();
        await archive.AppendAsync(Event(2, "order.failed"));
        await archive.AppendAsync(Event(3, "order.accepted"));
        var capture = Capture(0, 3);
        await archive.SaveCaptureAsync(capture);

        var reopened = new ReportArchive(_directory);
        (await reopened.AppendAsync(first)).ShouldBeFalse();
        var report = await reopened.QueryAsync(capture.Id, new(), "all.v1");

        report.Count.ShouldBe(3);
        report.CountsByEventType.Keys.ShouldBe(["order.accepted", "order.failed"]);
        report.CountsByEventType["order.accepted"].ShouldBe(2);
        report.CountsByEventType["order.failed"].ShouldBe(1);
        report.Completeness.ShouldBe("CompleteWithinReportingBoundary");
        report.AggregationVersion.ShouldBe("event-count.v1");
        report.Capture.Start.Sequence.ShouldBe(0);
        report.Capture.End.Sequence.ShouldBe(3);
        Directory.GetFiles(_directory, "*.pending", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Conflicting_duplicate_does_not_replace_original()
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Event(1, "original"));
        await Should.ThrowAsync<InvalidDataException>(() => archive.AppendAsync(Event(1, "replacement")));
        var capture = Capture(0, 1);
        await archive.SaveCaptureAsync(capture);

        var report = await archive.QueryAsync(capture.Id, new(), "all.v1");

        report.Count.ShouldBe(1);
        report.CountsByEventType.Keys.ShouldBe(["original"]);
        report.CountsByEventType.ContainsKey("replacement").ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_records_are_incomplete_even_when_filter_matches_nothing()
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Event(1));
        await archive.AppendAsync(Event(3));
        var capture = Capture(0, 3);
        await archive.SaveCaptureAsync(capture);

        var report = await archive.QueryAsync(capture.Id, TypeFilter("absent"), "absent.v1");

        report.Count.ShouldBe(0);
        report.CountsByEventType.ShouldBeEmpty();
        report.Completeness.ShouldBe("Incomplete");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public async Task Loss_metadata_marks_fully_persisted_capture_incomplete(long dropped, long rejected)
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Event(1));
        var capture = Capture(0, 1) with { Dropped = dropped, RejectedEvents = rejected };
        await archive.SaveCaptureAsync(capture);

        var report = await archive.QueryAsync(capture.Id, new(), "all.v1");

        report.Count.ShouldBe(1);
        report.Completeness.ShouldBe("Incomplete");
        report.Capture.Dropped.ShouldBe(dropped);
        report.Capture.RejectedEvents.ShouldBe(rejected);
    }

    [Fact]
    public async Task Frozen_capture_excludes_start_and_future_sequences_including_late_event_time()
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Event(1, "before"));
        await archive.AppendAsync(Event(2, "inside"));
        var capture = Capture(1, 2);
        await archive.SaveCaptureAsync(capture);
        var before = await archive.QueryAsync(capture.Id, new(), "all.v1");

        await archive.AppendAsync(Event(3, "late", EventTime.AddDays(-1)));
        var after = await archive.QueryAsync(capture.Id, new(), "all.v1");

        before.Count.ShouldBe(1);
        after.Count.ShouldBe(1);
        after.CountsByEventType.Keys.ShouldBe(["inside"]);
        after.Completeness.ShouldBe("CompleteWithinReportingBoundary");
        after.Capture.End.Sequence.ShouldBe(2);
    }

    [Fact]
    public async Task Serialized_filter_reuses_utc_half_open_bounds_for_live_and_historical_records()
    {
        var archive = new ReportArchive(_directory);
        var records = new[]
        {
            Event(1, "order.accepted", EventTime.AddTicks(-1)),
            Event(2, "order.accepted", EventTime),
            Event(3, "order.failed", EventTime.AddMinutes(30)),
            Event(4, "order.accepted", EventTime.AddHours(1).AddTicks(-1)),
            Event(5, "order.accepted", EventTime.AddHours(1))
        };
        foreach (var record in records) await archive.AppendAsync(record);
        var capture = Capture(0, 5);
        await archive.SaveCaptureAsync(capture);
        var definition = TypeFilter("order.accepted") with
        {
            FromInclusive = EventTime.ToOffset(TimeSpan.FromHours(2)),
            ToExclusive = EventTime.AddHours(1).ToOffset(TimeSpan.FromHours(-4))
        };
        var portable = JsonSerializer.Deserialize<ApplicationReportFilter>(JsonSerializer.Serialize(definition))!;

        var live = portable.Compile();
        records.Where(live.IsMatch).Select(record => record.Cursor.Sequence).ShouldBe([2L, 4L]);
        var report = await archive.QueryAsync(capture.Id, portable, "orders.v2");
        var storedFilter = JsonSerializer.Deserialize<ApplicationReportFilter>(report.FilterJson)!.Compile();

        report.Count.ShouldBe(2);
        report.CountsByEventType.Keys.ShouldBe(["order.accepted"]);
        report.CountsByEventType["order.accepted"].ShouldBe(2);
        report.FilterVersion.ShouldBe("orders.v2");
        report.Completeness.ShouldBe("CompleteWithinReportingBoundary");
        records.Where(storedFilter.IsMatch).Select(record => record.Cursor.Sequence).ShouldBe([2L, 4L]);
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("empty-stream")]
    [InlineData("different-stream")]
    [InlineData("negative-start")]
    [InlineData("reversed-range")]
    [InlineData("negative-dropped")]
    [InlineData("negative-rejected")]
    public async Task Invalid_capture_is_rejected_without_persisting_metadata(string invalidPart)
    {
        var archive = new ReportArchive(_directory);
        var capture = Capture(1, 2);
        capture = invalidPart switch
        {
            "empty-id" => capture with { Id = Guid.Empty },
            "empty-stream" => capture with { Start = new(Guid.Empty, 1), End = new(Guid.Empty, 2) },
            "different-stream" => capture with { End = new(Guid.NewGuid(), 2) },
            "negative-start" => capture with { Start = new(_streamId, -1) },
            "reversed-range" => capture with { End = new(_streamId, 0) },
            "negative-dropped" => capture with { Dropped = -1 },
            "negative-rejected" => capture with { RejectedEvents = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidPart))
        };

        await Should.ThrowAsync<ArgumentException>(() => archive.SaveCaptureAsync(capture));

        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelled_writes_leave_no_records_or_pending_files()
    {
        var archive = new ReportArchive(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => archive.AppendAsync(Event(1), cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => archive.SaveCaptureAsync(Capture(0, 1), cancellation.Token));

        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Oversized_records_are_rejected_before_persistence()
    {
        var archive = new ReportArchive(_directory);
        var record = Event(1) with { Message = new FlowEvent { Name = "large", Message = new string('x', 1024 * 1024) }.ToMessage() };

        await Should.ThrowAsync<ArgumentException>(() => archive.AppendAsync(record));

        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Oversized_archive_file_is_rejected_on_read()
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Event(1));
        var capture = Capture(0, 1);
        await archive.SaveCaptureAsync(capture);
        await File.WriteAllTextAsync(RecordPath(1), new string(' ', 1024 * 1024 + 1));

        var exception = await Should.ThrowAsync<InvalidDataException>(() => archive.QueryAsync(capture.Id, new(), "all.v1"));

        exception.Message.ShouldContain("size limit");
    }

    [Fact]
    public async Task Group_limit_allows_256_types_and_rejects_the_next()
    {
        var archive = new ReportArchive(_directory);
        for (var sequence = 1; sequence <= 257; sequence++)
            await archive.AppendAsync(Event(sequence, $"type.{sequence:D3}"));
        var allowed = Capture(0, 256);
        var exceeded = Capture(0, 257);
        await archive.SaveCaptureAsync(allowed);
        await archive.SaveCaptureAsync(exceeded);

        var report = await archive.QueryAsync(allowed.Id, new(), "all.v1");
        report.Count.ShouldBe(256);
        report.CountsByEventType.Count.ShouldBe(256);
        report.CountsByEventType["type.256"].ShouldBe(1);
        await Should.ThrowAsync<InvalidOperationException>(() => archive.QueryAsync(exceeded.Id, new(), "all.v1"));
        var narrowed = await archive.QueryAsync(exceeded.Id, TypeFilter("type.257"), "one.v1");
        narrowed.Count.ShouldBe(1);
        narrowed.CountsByEventType.Keys.ShouldBe(["type.257"]);
        narrowed.Completeness.ShouldBe("CompleteWithinReportingBoundary");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Archived_cursor_must_match_its_location(bool changeStream)
    {
        var archive = new ReportArchive(_directory);
        var record = Event(1);
        await archive.AppendAsync(record);
        var capture = Capture(0, 1);
        await archive.SaveCaptureAsync(capture);
        var changed = record with { Cursor = changeStream ? new(Guid.NewGuid(), 1) : new(_streamId, 2) };
        await File.WriteAllTextAsync(RecordPath(1), JsonSerializer.Serialize(changed));

        var exception = await Should.ThrowAsync<InvalidDataException>(() => archive.QueryAsync(capture.Id, new(), "all.v1"));

        exception.Message.ShouldContain("identity");
    }

    [Fact]
    public async Task Existing_capture_cannot_be_overwritten()
    {
        var archive = new ReportArchive(_directory);
        var capture = Capture(0, 2);
        await archive.SaveCaptureAsync(capture);

        await Should.ThrowAsync<IOException>(() => archive.SaveCaptureAsync(capture with { End = new(_streamId, 10) }));

        (await archive.ReadCaptureAsync(capture.Id)).End.Sequence.ShouldBe(2);
        Directory.GetFiles(_directory, "*.pending", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_identical_appends_commit_exactly_once()
    {
        var archive = new ReportArchive(_directory);
        var record = Event(1);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => archive.AppendAsync(record)));

        results.Count(committed => committed).ShouldBe(1);
        Directory.GetFiles(_directory, "*.json", SearchOption.AllDirectories).Length.ShouldBe(1);
        Directory.GetFiles(_directory, "*.pending", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    public async Task Invalid_event_cursor_is_rejected_without_writing(bool emptyStream, long sequence)
    {
        var archive = new ReportArchive(_directory);
        var record = Event(1) with { Cursor = new(emptyStream ? Guid.Empty : _streamId, sequence) };

        await Should.ThrowAsync<ArgumentException>(() => archive.AppendAsync(record));

        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, "CompleteWithinReportingBoundary")]
    [InlineData(1, "Incomplete")]
    public async Task Capture_without_a_stream_directory_reports_continuity_explicitly(long end, string completeness)
    {
        var archive = new ReportArchive(_directory);
        var capture = Capture(0, end);
        await archive.SaveCaptureAsync(capture);

        var report = await archive.QueryAsync(capture.Id, new(), "all.v1");

        report.Count.ShouldBe(0);
        report.CountsByEventType.ShouldBeEmpty();
        report.Completeness.ShouldBe(completeness);
    }

    [Fact]
    public async Task Capture_identity_must_match_its_requested_file()
    {
        var archive = new ReportArchive(_directory);
        var capture = Capture(0, 1);
        await archive.SaveCaptureAsync(capture);
        var path = Path.Combine(_directory, capture.Id.ToString("N") + ".capture.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(capture with { Id = Guid.NewGuid() }));

        var exception = await Should.ThrowAsync<InvalidDataException>(() => archive.ReadCaptureAsync(capture.Id));

        exception.Message.ShouldContain("identity");
    }

    [Fact]
    public async Task Cancelled_query_does_not_return_a_partial_report()
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Event(1));
        var capture = Capture(0, 1);
        await archive.SaveCaptureAsync(capture);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => archive.QueryAsync(capture.Id, new(), "all.v1", cancellation.Token));

        (await archive.QueryAsync(capture.Id, new(), "all.v1")).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(4, "CompleteWithinReportingBoundary")]
    [InlineData(5, "Incomplete")]
    public async Task Consumer_report_combines_custom_selection_with_portable_filter_and_preserves_capture_completeness(
        long captureEnd, string completeness)
    {
        var archive = new ReportArchive(_directory);
        var records = new[]
        {
            Measurement(1, "warehouse.fulfilled", 8, 3) with { Application = "north" },
            Measurement(2, "warehouse.fulfilled", 8, 3) with { Application = "south" },
            Measurement(3, "warehouse.fulfilled", 2, 3) with { Application = "north" },
            Measurement(4, "warehouse.received", 20, 3) with { Application = "north" }
        };
        foreach (var record in records) await archive.AppendAsync(record);
        var capture = Capture(0, captureEnd);
        await archive.SaveCaptureAsync(capture);
        var filter = new ApplicationReportFilter
        {
            Conditions = [new() { Path = "@application", Value = JsonSerializer.SerializeToElement("north") }]
        };

        var report = await archive.QueryAsync(capture.Id, new FulfilmentReport(), filter, "north.v2");
        var unscoped = await archive.QueryAsync(capture.Id, filter, "north.v2");

        report.Count.ShouldBe(1);
        report.CountsByEventType.Keys.ShouldBe(["warehouse.fulfilled"]);
        report.CountsByEventType["warehouse.fulfilled"].ShouldBe(1);
        unscoped.Count.ShouldBe(3);
        report.ReportType.ShouldBe("warehouse.fulfilled");
        report.ReportCategory.ShouldBe("fulfilment");
        report.ReportVersion.ShouldBe(3);
        report.FilterVersion.ShouldBe("north.v2");
        report.Capture.End.Sequence.ShouldBe(captureEnd);
        report.Completeness.ShouldBe(completeness);
        unscoped.Completeness.ShouldBe(completeness);
    }

    [Fact]
    public async Task Quantity_report_live_selection_and_static_generation_use_the_same_schema_and_filter()
    {
        var archive = new ReportArchive(_directory);
        var quantity = new QuantityReport(archive);
        var records = new[]
        {
            Measurement(1, "sample.measurement", 10),
            Measurement(2, "sample.measurement", 2),
            Measurement(3, "sample.measurement", 30, 2),
            Measurement(4, "other.measurement", 40)
        };
        foreach (var record in records) await archive.AppendAsync(record);
        var capture = Capture(0, 4);
        await archive.SaveCaptureAsync(capture);
        var filter = new ApplicationReportFilter
        {
            Conditions = [new()
            {
                Path = "/measurements/value", Comparison = ApplicationReportComparison.GreaterThanOrEqual,
                Value = JsonSerializer.SerializeToElement(10)
            }]
        };
        var compiled = filter.Compile();

        records.Where(quantity.Includes).Select(record => record.Cursor.Sequence).ShouldBe([1L, 2L]);
        records.Where(record => quantity.Includes(record) && compiled.IsMatch(record))
            .Select(record => record.Cursor.Sequence).ShouldBe([1L]);
        var report = await quantity.GenerateAsync(capture.Id, filter);

        report.Count.ShouldBe(1);
        report.CountsByEventType.Keys.ShouldBe(["sample.measurement"]);
        report.CountsByEventType["sample.measurement"].ShouldBe(1);
        report.ReportType.ShouldBe("sample.measurement");
        report.ReportCategory.ShouldBe("measurements");
        report.ReportVersion.ShouldBe(1);
        report.FilterVersion.ShouldBe("quantity.v1");
        report.Completeness.ShouldBe("CompleteWithinReportingBoundary");
        quantity.Descriptor.Fields.Single().Path.ShouldBe("/measurements/value");
        quantity.Descriptor.Fields.Single().Unit.ShouldBe("items");
    }

    [Fact]
    public async Task Unscoped_archive_query_keeps_report_metadata_absent()
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Measurement(1, "sample.measurement", 12));
        await archive.AppendAsync(Event(2, "unrelated"));
        var capture = Capture(0, 2);
        await archive.SaveCaptureAsync(capture);

        var report = await archive.QueryAsync(capture.Id, new(), "all.v1");

        report.Count.ShouldBe(2);
        report.CountsByEventType.Keys.ShouldBe(["sample.measurement", "unrelated"]);
        report.ReportType.ShouldBeNull();
        report.ReportCategory.ShouldBeNull();
        report.ReportVersion.ShouldBeNull();
        report.Completeness.ShouldBe("CompleteWithinReportingBoundary");
    }

    [Fact]
    public async Task Contiguous_archive_does_not_claim_completeness_when_source_completeness_is_unknown()
    {
        var archive = new ReportArchive(_directory);
        await archive.AppendAsync(Event(1));
        await archive.AppendAsync(Event(2));
        var capture = new ReportCapture
        {
            Id = Guid.NewGuid(), Start = new(_streamId, 0), End = new(_streamId, 2),
            Dropped = 0, RejectedEvents = 0
        };
        await archive.SaveCaptureAsync(capture);

        var report = await new ReportArchive(_directory).QueryAsync(capture.Id, new(), "all.v1");

        report.Count.ShouldBe(2);
        report.CountsByEventType["sample.event"].ShouldBe(2);
        report.Capture.SourceCompletenessKnown.ShouldBeFalse();
        report.Capture.Dropped.ShouldBe(0);
        report.Capture.RejectedEvents.ShouldBe(0);
        report.Completeness.ShouldBe("Incomplete");
    }

    private sealed class FulfilmentReport : IApplicationReport
    {
        public ApplicationReportDescriptor Descriptor { get; } = new()
        {
            Type = "warehouse.fulfilled", Category = "fulfilment", Version = 3,
            Description = "Fulfilled batches with at least five items"
        };

        public bool Includes(ApplicationReportEvent record)
            => record.SchemaVersion == Descriptor.Version &&
               record.Message.Headers.GetValueOrDefault(FlowEventHeaders.Type) == Descriptor.Type &&
               record.Message.Value is { } value &&
               value.ToJsonElement().GetProperty("measurements").GetProperty("value").GetDecimal() >= 5;
    }

    private ApplicationReportEvent Measurement(long sequence, string type, double value, int schemaVersion = 1)
        => Event(sequence, type) with
        {
            SchemaVersion = schemaVersion,
            Message = new FlowEvent
            {
                Name = type, Timestamp = EventTime,
                Measurements = new Dictionary<string, double> { ["value"] = value }
            }.ToMessage()
        };

    private ApplicationReportEvent Event(long sequence, string type = "sample.event", DateTimeOffset? time = null) => new()
    {
        Cursor = new(_streamId, sequence),
        ObservedAt = EventTime.AddMinutes(10),
        Application = "sample-application",
        RevisionId = "revision-1",
        Phase = ApplicationReportPhase.Active,
        Message = new FlowEvent { Name = type, Timestamp = time ?? EventTime }.ToMessage()
    };

    private ReportCapture Capture(long start, long end) => new()
    {
        Id = Guid.NewGuid(), Start = new(_streamId, start), End = new(_streamId, end), Dropped = 0, RejectedEvents = 0,
        SourceCompletenessKnown = true
    };

    private static ApplicationReportFilter TypeFilter(string type) => new()
    {
        Conditions = [new() { Path = "/type", Value = JsonSerializer.SerializeToElement(type) }]
    };

    private string RecordPath(long sequence) => Path.Combine(_directory, _streamId.ToString("N"),
        sequence.ToString("D20", CultureInfo.InvariantCulture) + ".json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
