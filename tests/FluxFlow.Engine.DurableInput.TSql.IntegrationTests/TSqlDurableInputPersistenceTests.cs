using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Engine.DurableInput.Tests;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlDurableInputPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_envelope_and_offsets_survive_store_reopen_exactly(bool isError)
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = isError
            ? TSqlDurableInputTestSupport.ErrorEnvelope("reopen-error")
            : TSqlDurableInputTestSupport.ValueEnvelope("reopen-value");

        await using (var writer = database.CreateStore())
            (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);

        await using var reader = database.CreateStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaManagement = TSqlDurableInputSchemaManagement.ValidateOnly
        });
        var lease = (await reader.LeaseAsync(new(
            "reopen-reader",
            envelope.EnqueuedAt,
            envelope.EnqueuedAt.AddMinutes(1),
            1))).ShouldHaveSingleItem();

        lease.Envelope.ShouldMatchExactly(envelope);
        lease.OwnerId.ShouldBe("reopen-reader");
        lease.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task Released_retry_schedule_attempt_and_failure_survive_store_reopen()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("release-reopen");
        var releasedAt = TSqlDurableInputTestSupport.Now;
        var retryAt = releasedAt.AddMinutes(10);
        var failure = new DurableInputFailure(
            DurableInputFailureKind.InputUnavailable,
            "receiver temporarily unavailable");
        await using (var writer = database.CreateStore())
        {
            var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(
                writer,
                envelope,
                now: releasedAt);
            (await writer.ReleaseAsync(new(
                envelope.Key,
                lease.LeaseToken,
                releasedAt,
                retryAt,
                failure))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }

        await using var reader = database.CreateStore();
        (await reader.LeaseAsync(new(
            "early-reader",
            retryAt.AddTicks(-1),
            retryAt.AddMinutes(1),
            1))).ShouldBeEmpty();
        var reacquired = (await reader.LeaseAsync(new(
            "due-reader",
            retryAt,
            retryAt.AddMinutes(1),
            1))).ShouldHaveSingleItem();

        reacquired.Attempt.ShouldBe(2);
        reacquired.Envelope.ShouldMatchExactly(envelope);
        (await database.ScalarAsync<int>(
            $"SELECT failure_kind FROM dbo.fluxflow_relational_inputs WHERE message_id = N'{envelope.MessageId.Value}';"))
            .ShouldBe((int)failure.Kind);
    }

    [Fact]
    public async Task Renewed_expiry_and_token_survive_store_reopen()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("renew-reopen");
        var leasedAt = TSqlDurableInputTestSupport.Now;
        var originalExpiry = leasedAt.AddMinutes(5);
        var renewedExpiry = leasedAt.AddMinutes(15);
        Guid token;
        await using (var writer = database.CreateStore())
        {
            (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
            var lease = (await writer.LeaseAsync(new(
                "renew-owner", leasedAt, originalExpiry, 1))).ShouldHaveSingleItem();
            token = lease.LeaseToken;
            (await writer.RenewLeaseAsync(new(
                envelope.Key,
                token,
                leasedAt.AddMinutes(1),
                renewedExpiry))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }

        await using var reader = database.CreateStore();
        (await reader.LeaseAsync(new(
            "too-early",
            originalExpiry,
            originalExpiry.AddMinutes(1),
            1))).ShouldBeEmpty();
        var recovered = (await reader.LeaseAsync(new(
            "recovery",
            renewedExpiry,
            renewedExpiry.AddMinutes(1),
            1))).ShouldHaveSingleItem();

        recovered.Attempt.ShouldBe(2);
        recovered.LeaseToken.ShouldNotBe(token);
        recovered.Envelope.ShouldMatchExactly(envelope);
    }

    [Fact]
    public async Task Delivered_and_dead_lettered_tombstones_survive_store_reopen()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var delivered = TSqlDurableInputTestSupport.ValueEnvelope("delivered-tombstone");
        var dead = TSqlDurableInputTestSupport.ErrorEnvelope("dead-tombstone");
        var failure = new DurableInputFailure(
            DurableInputFailureKind.InvalidEnvelope,
            "invalid envelope");
        await using (var writer = database.CreateStore())
        {
            var deliveredLease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(writer, delivered);
            (await writer.MarkDeliveredAsync(new(
                delivered.Key,
                deliveredLease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddMinutes(1))))
                .Status.ShouldBe(DurableInputTransitionStatus.Applied);
            var deadLease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(writer, dead);
            (await writer.DeadLetterAsync(new(
                dead.Key,
                deadLease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddMinutes(1),
                failure))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }

        await using var reader = database.CreateStore();
        (await reader.LeaseAsync(new(
            "future",
            TSqlDurableInputTestSupport.Now.AddYears(1),
            TSqlDurableInputTestSupport.Now.AddYears(1).AddMinutes(1),
            10))).ShouldBeEmpty();
        (await reader.GetAsync(delivered.Key)).ShouldBeNull();
        var details = await reader.GetAsync(dead.Key);
        details.ShouldNotBeNull();
        details.Envelope.ShouldMatchExactly(dead);
        details.Failure.ShouldBe(failure);
        details.Generation.ShouldBe(1);
    }

    [Fact]
    public async Task Large_payload_headers_error_details_and_failure_round_trip_without_provider_limit()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var large = new string('x', 70_000);
        var envelope = new DurableInputEnvelope(
            DurableInputStoreConformanceData.Input,
            "large-contract",
            isError: true,
            JsonSerializer.SerializeToElement<object?>(null),
            new FlowError(
                "large-error",
                large,
                "large-category",
                true,
                JsonSerializer.SerializeToElement(new { value = large })),
            new MessageId("large-round-trip"),
            new TraceId("large-trace"),
            TSqlDurableInputTestSupport.Now,
            TSqlDurableInputTestSupport.Now,
            null,
            null,
            new Dictionary<string, string> { ["large"] = large },
            DurableInputEnvelope.CurrentSchemaVersion);
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(store, envelope);
        var failure = new DurableInputFailure(DurableInputFailureKind.InvalidEnvelope, large);

        (await store.DeadLetterAsync(new(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1),
            failure))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
        var details = await store.GetAsync(envelope.Key);

        details.ShouldNotBeNull();
        details.Envelope.ShouldMatchExactly(envelope);
        details.Failure.Description.ShouldBe(large);
    }

    [Fact]
    public async Task Dead_letter_list_projects_metadata_without_materializing_corrupt_large_content()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("summary-projection");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(store, envelope);
        (await store.DeadLetterAsync(new(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1),
            new(DurableInputFailureKind.InvalidEnvelope, "invalid"))))
            .Status.ShouldBe(DurableInputTransitionStatus.Applied);
        await database.ExecuteAsync("""
            UPDATE dbo.fluxflow_relational_inputs
            SET payload_json = N'{not-json', headers_json = N'[]'
            WHERE message_id = N'summary-projection';
            """);

        var page = await store.ListAsync(new DurableInputDeadLetterQuery());

        page.Items.ShouldHaveSingleItem().Key.ShouldBe(envelope.Key);
        await Should.ThrowAsync<InvalidDataException>(() => store.GetAsync(envelope.Key).AsTask());
    }

    [Fact]
    public async Task Binary_key_ordering_is_independent_of_database_default_collation()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var expectedIds = new[] { "A", "a", "ä" }
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        foreach (var messageId in expectedIds.Reverse())
        {
            (await store.EnqueueAsync(DurableInputStoreConformanceData.Envelope(messageId)))
                .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        }

        var leases = await store.LeaseAsync(new(
            "ordinal-worker",
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Now.AddMinutes(1),
            expectedIds.Length));

        leases.Select(lease => lease.Envelope.MessageId.Value).ShouldBe(expectedIds);
    }

    [Fact]
    public async Task Reordered_json_object_content_is_idempotently_equivalent_without_overwrite()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var first = CopyWithPayload(
            TSqlDurableInputTestSupport.ValueEnvelope("json-order"),
            JsonSerializer.SerializeToElement(new { alpha = 1, beta = 2 }));
        var reordered = CopyWithPayload(
            first,
            JsonSerializer.SerializeToElement(new { beta = 2, alpha = 1 }));

        (await store.EnqueueAsync(first)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        (await store.EnqueueAsync(reordered)).Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        var stored = (await store.LeaseAsync(new(
            "json-reader",
            first.EnqueuedAt,
            first.EnqueuedAt.AddMinutes(1),
            1))).ShouldHaveSingleItem().Envelope;

        stored.Payload.GetRawText().ShouldBe(first.Payload.GetRawText());
        stored.Payload.GetRawText().ShouldNotBe(reordered.Payload.GetRawText());
    }

    [Fact]
    public async Task Dead_letter_generation_and_replay_schedule_survive_store_reopen()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("replay-reopen");
        var replayedAt = TSqlDurableInputTestSupport.Now.AddMinutes(2);
        var nextAttemptAt = replayedAt.AddMinutes(10);
        await using (var writer = database.CreateStore())
        {
            var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(writer, envelope);
            (await writer.DeadLetterAsync(new(
                envelope.Key,
                lease.LeaseToken,
                TSqlDurableInputTestSupport.Now.AddMinutes(1),
                new(DurableInputFailureKind.InvalidEnvelope, "invalid"))))
                .Status.ShouldBe(DurableInputTransitionStatus.Applied);
            var details = await writer.GetAsync(envelope.Key);
            details.ShouldNotBeNull();
            details.Generation.ShouldBe(1);
            (await writer.ReplayAsync(new(
                envelope.Key,
                details.Generation,
                replayedAt,
                nextAttemptAt))).Status.ShouldBe(DurableInputReplayStatus.Replayed);
        }

        await using var reader = database.CreateStore();
        (await reader.GetAsync(envelope.Key)).ShouldBeNull();
        (await reader.LeaseAsync(new(
            "early",
            nextAttemptAt.AddTicks(-1),
            nextAttemptAt.AddMinutes(1),
            1))).ShouldBeEmpty();
        var replayed = (await reader.LeaseAsync(new(
            "due",
            nextAttemptAt,
            nextAttemptAt.AddMinutes(1),
            1))).ShouldHaveSingleItem();

        replayed.Attempt.ShouldBe(1);
        replayed.Envelope.ShouldMatchExactly(envelope);
        (await database.ScalarAsync<long>(
            "SELECT dead_letter_generation FROM dbo.fluxflow_relational_inputs WHERE message_id = N'replay-reopen';"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Corrupt_persisted_failure_enum_fails_with_stable_row_context()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var envelope = TSqlDurableInputTestSupport.ValueEnvelope("corrupt-enum");
        var lease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(store, envelope);
        (await store.DeadLetterAsync(new(
            envelope.Key,
            lease.LeaseToken,
            TSqlDurableInputTestSupport.Now.AddMinutes(1),
            new(DurableInputFailureKind.InvalidEnvelope, "invalid"))))
            .Status.ShouldBe(DurableInputTransitionStatus.Applied);
        await database.ExecuteAsync("""
            UPDATE dbo.fluxflow_relational_inputs
            SET failure_kind = 999
            WHERE message_id = N'corrupt-enum';
            """);

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.ListAsync(new DurableInputDeadLetterQuery()).AsTask());

        exception.Message.ShouldContain(envelope.Key.ToString());
        exception.Message.ShouldContain("failure kind value 999 is invalid");
    }

    [Fact]
    public async Task Disposing_one_store_does_not_clear_shared_pools_or_break_another_instance()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var first = database.CreateStore();
        await using var second = database.CreateStore();
        var firstEnvelope = TSqlDurableInputTestSupport.ValueEnvelope("dispose-a");
        var secondEnvelope = TSqlDurableInputTestSupport.ValueEnvelope("dispose-b");

        (await first.EnqueueAsync(firstEnvelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        await first.DisposeAsync();
        (await second.EnqueueAsync(secondEnvelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);

        (await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs;"))
            .ShouldBe(2);
    }

    private static DurableInputEnvelope CopyWithPayload(
        DurableInputEnvelope source,
        JsonElement payload)
        => new(
            source.Address,
            source.ContractName,
            isError: false,
            payload,
            error: null,
            source.MessageId,
            source.TraceId,
            source.Timestamp,
            source.EnqueuedAt,
            source.CorrelationId,
            source.CausationId,
            source.Headers,
            source.SchemaVersion);
}
