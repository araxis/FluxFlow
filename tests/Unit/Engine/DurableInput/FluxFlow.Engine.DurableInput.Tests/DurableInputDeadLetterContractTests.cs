using System.Reflection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputDeadLetterContractTests
{
    [Fact]
    public void Query_exposes_exact_defaults_and_preserves_every_filter()
    {
        var cursor = new DurableInputDeadLetterCursor(
            DurableInputStoreConformanceData.Now.AddMinutes(-1),
            DurableInputStoreConformanceData.Envelope("cursor").Key);

        var defaults = new DurableInputDeadLetterQuery();
        var filtered = new DurableInputDeadLetterQuery(
            DurableInputStoreConformanceData.Input,
            DurableInputFailureKind.UnknownContract,
            DurableInputStoreConformanceData.Now.AddHours(-2),
            DurableInputStoreConformanceData.Now,
            cursor,
            DurableInputDeadLetterQuery.MaximumPageSize);

        defaults.Address.ShouldBeNull();
        defaults.FailureKind.ShouldBeNull();
        defaults.DeadLetteredFrom.ShouldBeNull();
        defaults.DeadLetteredBefore.ShouldBeNull();
        defaults.Cursor.ShouldBeNull();
        defaults.PageSize.ShouldBe(DurableInputDeadLetterQuery.DefaultPageSize);
        DurableInputDeadLetterQuery.DefaultPageSize.ShouldBe(50);
        DurableInputDeadLetterQuery.MaximumPageSize.ShouldBe(200);
        filtered.Address.ShouldBe(DurableInputStoreConformanceData.Input);
        filtered.FailureKind.ShouldBe(DurableInputFailureKind.UnknownContract);
        filtered.DeadLetteredFrom.ShouldBe(DurableInputStoreConformanceData.Now.AddHours(-2));
        filtered.DeadLetteredBefore.ShouldBe(DurableInputStoreConformanceData.Now);
        filtered.Cursor.ShouldBeSameAs(cursor);
        filtered.PageSize.ShouldBe(DurableInputDeadLetterQuery.MaximumPageSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(DurableInputDeadLetterQuery.MaximumPageSize)]
    public void Query_accepts_inclusive_page_size_boundaries(int pageSize)
    {
        new DurableInputDeadLetterQuery(pageSize: pageSize).PageSize.ShouldBe(pageSize);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(DurableInputDeadLetterQuery.MaximumPageSize + 1)]
    [InlineData(int.MaxValue)]
    public void Query_rejects_page_sizes_outside_the_inclusive_boundaries(int pageSize)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputDeadLetterQuery(pageSize: pageSize))
            .ParamName.ShouldBe("pageSize");
    }

    [Fact]
    public void Query_rejects_unknown_failure_kind_and_nonascending_time_ranges()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputDeadLetterQuery(failureKind: (DurableInputFailureKind)int.MaxValue))
            .ParamName.ShouldBe("failureKind");

        Should.Throw<ArgumentException>(() =>
                new DurableInputDeadLetterQuery(
                    deadLetteredFrom: DurableInputStoreConformanceData.Now,
                    deadLetteredBefore: DurableInputStoreConformanceData.Now))
            .ParamName.ShouldBe("deadLetteredFrom");
        Should.Throw<ArgumentException>(() =>
                new DurableInputDeadLetterQuery(
                    deadLetteredFrom: DurableInputStoreConformanceData.Now.AddTicks(1),
                    deadLetteredBefore: DurableInputStoreConformanceData.Now))
            .ParamName.ShouldBe("deadLetteredFrom");
    }

    [Fact]
    public void Cursor_requires_a_complete_key_and_preserves_the_exact_position()
    {
        var envelope = DurableInputStoreConformanceData.Envelope("cursor-key");

        var cursor = new DurableInputDeadLetterCursor(
            DurableInputStoreConformanceData.Now,
            envelope.Key);

        cursor.DeadLetteredAt.ShouldBe(DurableInputStoreConformanceData.Now);
        cursor.Key.ShouldBe(envelope.Key);
        Should.Throw<ArgumentException>(() =>
                new DurableInputDeadLetterCursor(DurableInputStoreConformanceData.Now, default))
            .ParamName.ShouldBe("key");
    }

    [Fact]
    public void Summary_preserves_the_exact_payload_free_operational_metadata()
    {
        var envelope = DurableInputStoreConformanceData.Envelope(
            "summary",
            contractName: "orders-v7",
            schemaVersion: 7);
        var summary = Summary(
            envelope,
            DurableInputFailureKind.DeserializationFailed,
            attempt: 4,
            deadLetteredAt: DurableInputStoreConformanceData.Now,
            generation: 9);

        summary.Key.ShouldBe(envelope.Key);
        summary.ContractName.ShouldBe("orders-v7");
        summary.EnvelopeSchemaVersion.ShouldBe(7);
        summary.IsError.ShouldBeFalse();
        summary.EnqueuedAt.ShouldBe(envelope.EnqueuedAt);
        summary.Attempt.ShouldBe(4);
        summary.FailureKind.ShouldBe(DurableInputFailureKind.DeserializationFailed);
        summary.DeadLetteredAt.ShouldBe(DurableInputStoreConformanceData.Now);
        summary.Generation.ShouldBe(9);

        typeof(DurableInputDeadLetterSummary)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(new[]
            {
                "Attempt",
                "ContractName",
                "DeadLetteredAt",
                "EnqueuedAt",
                "EnvelopeSchemaVersion",
                "FailureKind",
                "Generation",
                "IsError",
                "Key"
            });
    }

    [Fact]
    public void Summary_rejects_each_invalid_key_contract_count_failure_and_generation()
    {
        var envelope = DurableInputStoreConformanceData.Envelope("invalid-summary");
        DurableInputDeadLetterSummary Create(
            DurableInputKey key,
            string contractName,
            int envelopeSchemaVersion,
            int attempt,
            DurableInputFailureKind failureKind,
            long generation)
            => new(
                key,
                contractName,
                envelopeSchemaVersion,
                envelope.IsError,
                envelope.EnqueuedAt,
                attempt,
                failureKind,
                DurableInputStoreConformanceData.Now,
                generation);

        Should.Throw<ArgumentException>(() =>
                Create(default, envelope.ContractName, envelope.SchemaVersion, 1, DurableInputFailureKind.InputUnavailable, 1))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentException>(() =>
                Create(envelope.Key, null!, envelope.SchemaVersion, 1, DurableInputFailureKind.InputUnavailable, 1))
            .ParamName.ShouldBe("contractName");
        Should.Throw<ArgumentException>(() =>
                Create(envelope.Key, "  ", envelope.SchemaVersion, 1, DurableInputFailureKind.InputUnavailable, 1))
            .ParamName.ShouldBe("contractName");
        Should.Throw<ArgumentException>(() =>
                Create(envelope.Key, " orders-v1 ", envelope.SchemaVersion, 1, DurableInputFailureKind.InputUnavailable, 1))
            .ParamName.ShouldBe("contractName");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                Create(envelope.Key, envelope.ContractName, 0, 1, DurableInputFailureKind.InputUnavailable, 1))
            .ParamName.ShouldBe("envelopeSchemaVersion");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                Create(envelope.Key, envelope.ContractName, envelope.SchemaVersion, 0, DurableInputFailureKind.InputUnavailable, 1))
            .ParamName.ShouldBe("attempt");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                Create(envelope.Key, envelope.ContractName, envelope.SchemaVersion, 1, (DurableInputFailureKind)0, 1))
            .ParamName.ShouldBe("failureKind");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                Create(envelope.Key, envelope.ContractName, envelope.SchemaVersion, 1, DurableInputFailureKind.InputUnavailable, 0))
            .ParamName.ShouldBe("generation");
    }

    [Fact]
    public void Page_copies_items_and_derives_continuation_from_the_exact_last_item()
    {
        var first = Summary(DurableInputStoreConformanceData.Envelope("page-1"));
        var last = Summary(
            DurableInputStoreConformanceData.Envelope("page-2"),
            deadLetteredAt: DurableInputStoreConformanceData.Now.AddMinutes(-1));
        var source = new List<DurableInputDeadLetterSummary> { first, last };
        var cursor = new DurableInputDeadLetterCursor(last.DeadLetteredAt, last.Key);

        var page = new DurableInputDeadLetterPage(source, cursor);
        source.Clear();

        page.Items.ShouldBe(new[] { first, last });
        page.NextCursor.ShouldBe(cursor);
        page.HasMore.ShouldBeTrue();

        var terminal = new DurableInputDeadLetterPage([last], nextCursor: null);
        terminal.Items.ShouldHaveSingleItem().ShouldBe(last);
        terminal.NextCursor.ShouldBeNull();
        terminal.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Page_rejects_null_items_empty_continuation_and_mismatched_last_position()
    {
        var item = Summary(DurableInputStoreConformanceData.Envelope("page-validation"));
        var exactCursor = new DurableInputDeadLetterCursor(item.DeadLetteredAt, item.Key);

        Should.Throw<ArgumentNullException>(() =>
                new DurableInputDeadLetterPage(null!, nextCursor: null))
            .ParamName.ShouldBe("items");
        Should.Throw<ArgumentException>(() =>
                new DurableInputDeadLetterPage(
                    new[] { (DurableInputDeadLetterSummary)null! },
                    nextCursor: null))
            .ParamName.ShouldBe("items");
        Should.Throw<ArgumentException>(() =>
                new DurableInputDeadLetterPage([], exactCursor))
            .ParamName.ShouldBe("nextCursor");
        Should.Throw<ArgumentException>(() =>
                new DurableInputDeadLetterPage(
                    [item],
                    new DurableInputDeadLetterCursor(item.DeadLetteredAt.AddTicks(1), item.Key)))
            .ParamName.ShouldBe("nextCursor");
        Should.Throw<ArgumentException>(() =>
                new DurableInputDeadLetterPage(
                    [item],
                    new DurableInputDeadLetterCursor(
                        item.DeadLetteredAt,
                        DurableInputStoreConformanceData.Envelope("other-key").Key)))
            .ParamName.ShouldBe("nextCursor");
    }

    [Fact]
    public void Details_preserve_the_complete_envelope_and_reject_invalid_operational_metadata()
    {
        var envelope = DurableInputStoreConformanceData.Envelope("details");
        var failure = DurableInputStoreConformanceData.Failure();

        var details = new DurableInputDeadLetterDetails(
            envelope,
            attempt: 3,
            failure,
            DurableInputStoreConformanceData.Now,
            generation: 8);

        details.Envelope.ShouldBeSameAs(envelope);
        details.Attempt.ShouldBe(3);
        details.Failure.ShouldBeSameAs(failure);
        details.DeadLetteredAt.ShouldBe(DurableInputStoreConformanceData.Now);
        details.Generation.ShouldBe(8);
        Should.Throw<ArgumentNullException>(() =>
                new DurableInputDeadLetterDetails(null!, 1, failure, DurableInputStoreConformanceData.Now, 1))
            .ParamName.ShouldBe("envelope");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputDeadLetterDetails(envelope, 0, failure, DurableInputStoreConformanceData.Now, 1))
            .ParamName.ShouldBe("attempt");
        Should.Throw<ArgumentNullException>(() =>
                new DurableInputDeadLetterDetails(envelope, 1, null!, DurableInputStoreConformanceData.Now, 1))
            .ParamName.ShouldBe("failure");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputDeadLetterDetails(envelope, 1, failure, DurableInputStoreConformanceData.Now, 0))
            .ParamName.ShouldBe("generation");
    }

    [Fact]
    public void Replay_preserves_the_exact_command_and_rejects_invalid_generation_key_and_schedule()
    {
        var envelope = DurableInputStoreConformanceData.Envelope("replay");
        var replayedAt = DurableInputStoreConformanceData.Now;
        var nextAttemptAt = replayedAt.AddMinutes(3);

        var replay = new DurableInputReplay(
            envelope.Key,
            expectedGeneration: 4,
            replayedAt,
            nextAttemptAt);

        replay.Key.ShouldBe(envelope.Key);
        replay.ExpectedGeneration.ShouldBe(4);
        replay.ReplayedAt.ShouldBe(replayedAt);
        replay.NextAttemptAt.ShouldBe(nextAttemptAt);
        new DurableInputReplay(envelope.Key, 1, replayedAt, replayedAt)
            .NextAttemptAt.ShouldBe(replayedAt);
        Should.Throw<ArgumentException>(() =>
                new DurableInputReplay(default, 1, replayedAt, replayedAt))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputReplay(envelope.Key, 0, replayedAt, replayedAt))
            .ParamName.ShouldBe("expectedGeneration");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputReplay(envelope.Key, 1, replayedAt, replayedAt.AddTicks(-1)))
            .ParamName.ShouldBe("nextAttemptAt");
    }

    [Theory]
    [InlineData(DurableInputReplayStatus.Replayed, true)]
    [InlineData(DurableInputReplayStatus.NotFound, false)]
    [InlineData(DurableInputReplayStatus.NotDeadLettered, false)]
    [InlineData(DurableInputReplayStatus.GenerationMismatch, false)]
    public void Replay_result_exposes_each_exact_status(
        DurableInputReplayStatus status,
        bool isReplayed)
    {
        var key = DurableInputStoreConformanceData.Envelope("replay-result").Key;

        var result = new DurableInputReplayResult(key, status);

        result.Key.ShouldBe(key);
        result.Status.ShouldBe(status);
        result.IsReplayed.ShouldBe(isReplayed);
    }

    [Fact]
    public void Replay_result_rejects_incomplete_keys_and_unknown_status_values()
    {
        var key = DurableInputStoreConformanceData.Envelope("invalid-replay-result").Key;

        Should.Throw<ArgumentException>(() =>
                new DurableInputReplayResult(default, DurableInputReplayStatus.Replayed))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputReplayResult(key, (DurableInputReplayStatus)0))
            .ParamName.ShouldBe("status");
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputReplayResult(key, (DurableInputReplayStatus)int.MaxValue))
            .ParamName.ShouldBe("status");
    }

    private static DurableInputDeadLetterSummary Summary(
        DurableInputEnvelope envelope,
        DurableInputFailureKind failureKind = DurableInputFailureKind.InputUnavailable,
        DurableInputKey? key = null,
        string? contractName = null,
        int? envelopeSchemaVersion = null,
        int attempt = 1,
        DateTimeOffset? deadLetteredAt = null,
        long generation = 1)
        => new(
            key ?? envelope.Key,
            contractName ?? envelope.ContractName,
            envelopeSchemaVersion ?? envelope.SchemaVersion,
            envelope.IsError,
            envelope.EnqueuedAt,
            attempt,
            failureKind,
            deadLetteredAt ?? DurableInputStoreConformanceData.Now,
            generation);
}
