using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputDeadLetterContractTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 7, 31, 9, 10, 11, TimeSpan.FromHours(2));

    private static readonly DateTimeOffset DeadLetteredAt =
        new(2026, 8, 1, 10, 20, 30, TimeSpan.FromHours(-4));

    [Fact]
    public void Query_defaults_and_boundaries_preserve_exact_filters()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var cursor = new DurableOutputDeadLetterCursor(DeadLetteredAt, key);
        var defaults = new DurableOutputDeadLetterQuery();
        var query = new DurableOutputDeadLetterQuery(
            key.Address,
            DurableOutputDeadLetterReason.HandlerFailure,
            DeadLetteredAt.AddDays(-1),
            DeadLetteredAt,
            cursor,
            DurableOutputDeadLetterQuery.MaximumPageSize);

        defaults.Address.ShouldBeNull();
        defaults.Reason.ShouldBeNull();
        defaults.DeadLetteredFrom.ShouldBeNull();
        defaults.DeadLetteredBefore.ShouldBeNull();
        defaults.Cursor.ShouldBeNull();
        defaults.PageSize.ShouldBe(DurableOutputDeadLetterQuery.DefaultPageSize);
        query.Address.ShouldBe(key.Address);
        query.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        query.DeadLetteredFrom.ShouldBe(DeadLetteredAt.AddDays(-1));
        query.DeadLetteredBefore.ShouldBe(DeadLetteredAt);
        query.Cursor.ShouldBeSameAs(cursor);
        query.PageSize.ShouldBe(DurableOutputDeadLetterQuery.MaximumPageSize);
        new DurableOutputDeadLetterQuery(pageSize: 1).PageSize.ShouldBe(1);
    }

    [Fact]
    public void Query_rejects_undefined_reason_invalid_page_size_and_invalid_time_range()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeadLetterQuery(
            reason: (DurableOutputDeadLetterReason)99)).ParamName.ShouldBe("reason");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeadLetterQuery(pageSize: 0)).ParamName.ShouldBe("pageSize");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeadLetterQuery(
            pageSize: DurableOutputDeadLetterQuery.MaximumPageSize + 1)).ParamName.ShouldBe("pageSize");
        Should.Throw<ArgumentException>(() => new DurableOutputDeadLetterQuery(
            deadLetteredFrom: DeadLetteredAt,
            deadLetteredBefore: DeadLetteredAt)).ParamName.ShouldBe("deadLetteredFrom");
        Should.Throw<ArgumentException>(() => new DurableOutputDeadLetterQuery(
            deadLetteredFrom: DeadLetteredAt.AddTicks(1),
            deadLetteredBefore: DeadLetteredAt)).ParamName.ShouldBe("deadLetteredFrom");
    }

    [Fact]
    public void Cursor_requires_valid_key_and_preserves_exact_offset()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var cursor = new DurableOutputDeadLetterCursor(DeadLetteredAt, key);

        cursor.Key.ShouldBe(key);
        cursor.DeadLetteredAt.ShouldBe(DeadLetteredAt);
        cursor.DeadLetteredAt.Offset.ShouldBe(DeadLetteredAt.Offset);
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeadLetterCursor(DeadLetteredAt, default)).ParamName.ShouldBe("key");
    }

    [Fact]
    public void Summary_preserves_metadata_only_exact_values_and_rejects_invalid_inputs()
    {
        var summary = Summary();

        summary.Key.ShouldBe(DurableOutputTestData.Envelope().Key);
        summary.ContractName.ShouldBe("order.completed-v2");
        summary.EnvelopeSchemaVersion.ShouldBe(2);
        summary.IsError.ShouldBeTrue();
        summary.CapturedAt.ShouldBe(CapturedAt);
        summary.CapturedAt.Offset.ShouldBe(CapturedAt.Offset);
        summary.Attempt.ShouldBe(3);
        summary.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        summary.DeadLetteredAt.ShouldBe(DeadLetteredAt);
        summary.Generation.ShouldBe(4);
        typeof(DurableOutputDeadLetterSummary).GetProperties().Select(static property => property.Name)
            .ShouldBe([
                "Key", "ContractName", "EnvelopeSchemaVersion", "IsError", "CapturedAt",
                "Attempt", "Reason", "DeadLetteredAt", "Generation"
            ], ignoreOrder: true);

        Should.Throw<ArgumentException>(() => new DurableOutputDeadLetterSummary(
            default,
            "order.completed-v2",
            2,
            true,
            CapturedAt,
            3,
            DurableOutputDeadLetterReason.HandlerFailure,
            DeadLetteredAt,
            4)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentException>(() => Summary(contractName: " contract"))
            .ParamName.ShouldBe("contractName");
        Should.Throw<ArgumentOutOfRangeException>(() => Summary(envelopeSchemaVersion: 0))
            .ParamName.ShouldBe("envelopeSchemaVersion");
        Should.Throw<ArgumentOutOfRangeException>(() => Summary(attempt: 0))
            .ParamName.ShouldBe("attempt");
        Should.Throw<ArgumentOutOfRangeException>(() => Summary(
            reason: (DurableOutputDeadLetterReason)99)).ParamName.ShouldBe("reason");
        Should.Throw<ArgumentOutOfRangeException>(() => Summary(generation: 0))
            .ParamName.ShouldBe("generation");
    }

    [Fact]
    public void Page_snapshots_items_is_immutable_and_exposes_exact_next_cursor()
    {
        var summary = Summary();
        var source = new List<DurableOutputDeadLetterSummary> { summary };
        var cursor = new DurableOutputDeadLetterCursor(summary.DeadLetteredAt, summary.Key);
        var page = new DurableOutputDeadLetterPage(source, cursor);
        source.Clear();

        page.Items.ShouldBe([summary]);
        page.NextCursor.ShouldBeSameAs(cursor);
        page.HasMore.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() =>
            ((IList<DurableOutputDeadLetterSummary>)page.Items).Add(Summary(generation: 5)));
        new DurableOutputDeadLetterPage([], null).HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Page_rejects_null_items_empty_cursor_and_nonmatching_last_cursor()
    {
        var summary = Summary();
        var other = Summary(generation: 5, deadLetteredAt: DeadLetteredAt.AddTicks(-1));

        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputDeadLetterPage(null!, null)).ParamName.ShouldBe("items");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeadLetterPage([null!], null)).ParamName.ShouldBe("items");
        Should.Throw<ArgumentException>(() => new DurableOutputDeadLetterPage(
            [],
            new DurableOutputDeadLetterCursor(summary.DeadLetteredAt, summary.Key)))
            .ParamName.ShouldBe("nextCursor");
        Should.Throw<ArgumentException>(() => new DurableOutputDeadLetterPage(
            [summary],
            new DurableOutputDeadLetterCursor(other.DeadLetteredAt, other.Key)))
            .ParamName.ShouldBe("nextCursor");
        var sameInstantDifferentOffset = summary.DeadLetteredAt.ToOffset(TimeSpan.Zero);
        Should.Throw<ArgumentException>(() => new DurableOutputDeadLetterPage(
            [summary],
            new DurableOutputDeadLetterCursor(sameInstantDifferentOffset, summary.Key)))
            .ParamName.ShouldBe("nextCursor");
    }

    [Fact]
    public void Details_preserve_complete_envelope_and_reject_invalid_inputs()
    {
        var envelope = DurableOutputTestData.Envelope();
        var details = new DurableOutputDeadLetterDetails(
            envelope, 3, DurableOutputDeadLetterReason.HandlerFailure, DeadLetteredAt, 4);

        details.Envelope.ShouldBeSameAs(envelope);
        details.Attempt.ShouldBe(3);
        details.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        details.DeadLetteredAt.ShouldBe(DeadLetteredAt);
        details.Generation.ShouldBe(4);
        Should.Throw<ArgumentNullException>(() => new DurableOutputDeadLetterDetails(
            null!, 1, DurableOutputDeadLetterReason.HandlerFailure, DeadLetteredAt, 1))
            .ParamName.ShouldBe("envelope");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeadLetterDetails(
            envelope, 0, DurableOutputDeadLetterReason.HandlerFailure, DeadLetteredAt, 1))
            .ParamName.ShouldBe("attempt");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeadLetterDetails(
            envelope, 1, (DurableOutputDeadLetterReason)99, DeadLetteredAt, 1))
            .ParamName.ShouldBe("reason");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeadLetterDetails(
            envelope, 1, DurableOutputDeadLetterReason.HandlerFailure, DeadLetteredAt, 0))
            .ParamName.ShouldBe("generation");
    }

    [Fact]
    public void Replay_preserves_exact_values_and_rejects_invalid_key_generation_schedule()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var next = DeadLetteredAt.AddSeconds(5);
        var replay = new DurableOutputReplay(key, 4, DeadLetteredAt, next);

        replay.Key.ShouldBe(key);
        replay.ExpectedGeneration.ShouldBe(4);
        replay.ReplayedAt.ShouldBe(DeadLetteredAt);
        replay.NextAttemptAt.ShouldBe(next);
        new DurableOutputReplay(key, 1, DeadLetteredAt, DeadLetteredAt)
            .NextAttemptAt.ShouldBe(DeadLetteredAt);
        Should.Throw<ArgumentException>(() => new DurableOutputReplay(
            default, 1, DeadLetteredAt, DeadLetteredAt)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputReplay(
            key, 0, DeadLetteredAt, DeadLetteredAt)).ParamName.ShouldBe("expectedGeneration");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputReplay(
            key, 1, DeadLetteredAt, DeadLetteredAt.AddTicks(-1)))
            .ParamName.ShouldBe("nextAttemptAt");
    }

    [Theory]
    [InlineData(DurableOutputReplayStatus.Replayed, true)]
    [InlineData(DurableOutputReplayStatus.NotFound, false)]
    [InlineData(DurableOutputReplayStatus.NotDeadLettered, false)]
    [InlineData(DurableOutputReplayStatus.GenerationMismatch, false)]
    public void Replay_result_exposes_all_exact_statuses_and_rejects_invalid_values(
        DurableOutputReplayStatus status,
        bool isReplayed)
    {
        var key = DurableOutputTestData.Envelope().Key;
        var result = new DurableOutputReplayResult(key, status);

        result.Key.ShouldBe(key);
        result.Status.ShouldBe(status);
        result.IsReplayed.ShouldBe(isReplayed);
        Should.Throw<ArgumentException>(() =>
            new DurableOutputReplayResult(default, status)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputReplayResult(key, (DurableOutputReplayStatus)99))
            .ParamName.ShouldBe("status");
    }

    [Fact]
    public void Dead_letter_contracts_are_immutable_bounded_and_operationally_separate()
    {
        var immutableTypes = new[]
        {
            typeof(DurableOutputDeadLetterQuery),
            typeof(DurableOutputDeadLetterCursor),
            typeof(DurableOutputDeadLetterSummary),
            typeof(DurableOutputDeadLetterPage),
            typeof(DurableOutputDeadLetterDetails),
            typeof(DurableOutputReplay),
            typeof(DurableOutputReplayResult)
        };

        foreach (var type in immutableTypes)
        {
            type.GetProperties().ShouldAllBe(
                static property => property.SetMethod == null,
                $"{type.Name} must not expose mutable properties");
        }

        DurableOutputDeadLetterQuery.DefaultPageSize.ShouldBeGreaterThan(0);
        DurableOutputDeadLetterQuery.MaximumPageSize
            .ShouldBeGreaterThanOrEqualTo(DurableOutputDeadLetterQuery.DefaultPageSize);
        typeof(IDurableOutputDeadLetterStore).GetInterfaces().ShouldBeEmpty();
        typeof(IDurableOutputDeadLetterStore).GetMethods().Select(static method => method.Name)
            .ShouldBe(["ListAsync", "GetAsync", "ReplayAsync"], ignoreOrder: true);
        typeof(IDurableOutputStore).GetInterfaces().ShouldNotContain(typeof(IDurableOutputDeadLetterStore));
        typeof(IDurableOutputDeliveryStore).GetInterfaces()
            .ShouldNotContain(typeof(IDurableOutputDeadLetterStore));
    }

    private static DurableOutputDeadLetterSummary Summary(
        DurableOutputKey? key = null,
        string contractName = "order.completed-v2",
        int envelopeSchemaVersion = 2,
        int attempt = 3,
        DurableOutputDeadLetterReason reason = DurableOutputDeadLetterReason.HandlerFailure,
        DateTimeOffset? deadLetteredAt = null,
        long generation = 4)
        => new(
            key ?? DurableOutputTestData.Envelope().Key,
            contractName,
            envelopeSchemaVersion,
            isError: true,
            CapturedAt,
            attempt,
            reason,
            deadLetteredAt ?? DeadLetteredAt,
            generation);
}
