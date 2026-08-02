using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputStorePersistenceTests
{
    [Fact]
    public async Task Complete_value_envelope_round_trips_exactly_after_store_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var expected = SqlFileDurableInputTestData.CompleteValueEnvelope();
        await using (var writer = database.CreateStore())
        {
            var result = await writer.EnqueueAsync(expected);
            result.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        }

        await using var reader = database.CreateStore();
        var lease = (await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: expected.EnqueuedAt.AddMinutes(1),
            leaseUntil: expected.EnqueuedAt.AddMinutes(2)))).Single();

        lease.Envelope.ShouldMatchEnvelope(expected);
        lease.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task Complete_error_envelope_round_trips_exactly_after_store_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var expected = SqlFileDurableInputTestData.CompleteErrorEnvelope();
        await using (var writer = database.CreateStore())
        {
            var result = await writer.EnqueueAsync(expected);
            result.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        }

        await using var reader = database.CreateStore();
        var lease = (await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: expected.EnqueuedAt.AddMinutes(1),
            leaseUntil: expected.EnqueuedAt.AddMinutes(2)))).Single();

        lease.Envelope.ShouldMatchEnvelope(expected);
        lease.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task Released_retry_schedule_attempt_and_failure_survive_store_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope(
            messageId: "released-persistent",
            enqueuedAt: DurableInputStoreConformanceData.Now.AddMinutes(-1));
        var dueAt = DurableInputStoreConformanceData.Now.AddMinutes(5);
        var failure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.InputUnavailable,
            "current revision is unavailable");
        await using (var writer = database.CreateStore())
        {
            await writer.EnqueueAsync(envelope);
            var lease = (await writer.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
            var released = await writer.ReleaseAsync(new DurableInputRelease(
                envelope.Key,
                lease.LeaseToken,
                DurableInputStoreConformanceData.Now.AddSeconds(1),
                dueAt,
                failure));
            released.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }

        await using (var connection = await OpenAsync(database.DatabasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT state, attempt, next_attempt_utc_ticks, failure_kind, failure_description
                FROM fluxflow_durable_inputs
                WHERE message_id = 'released-persistent';
                """;
            await using var row = await command.ExecuteReaderAsync();
            (await row.ReadAsync()).ShouldBeTrue();
            row.GetInt32(0).ShouldBe((int)DurableInputState.Pending);
            row.GetInt32(1).ShouldBe(1);
            row.GetInt64(2).ShouldBe(dueAt.UtcTicks);
            row.GetInt32(3).ShouldBe((int)failure.Kind);
            row.GetString(4).ShouldBe(failure.Description);
            (await row.ReadAsync()).ShouldBeFalse();
        }

        await using var reader = database.CreateStore();
        var beforeDue = await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: dueAt.AddTicks(-1),
            leaseUntil: dueAt.AddSeconds(30)));
        var atDue = (await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: dueAt,
            leaseUntil: dueAt.AddSeconds(30)))).Single();

        beforeDue.ShouldBeEmpty();
        atDue.Envelope.ShouldMatchEnvelope(envelope);
        atDue.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task Active_lease_remains_exclusive_after_reopen_and_expires_at_the_exact_boundary()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "leased-persistent");
        DurableInputLease originalLease;
        await using (var writer = database.CreateStore())
        {
            await writer.EnqueueAsync(envelope);
            originalLease = (await writer.LeaseAsync(DurableInputStoreConformanceData.Request(
                ownerId: "owner-a",
                leaseUntil: DurableInputStoreConformanceData.Now.AddMinutes(2)))).Single();
        }

        await using var reader = database.CreateStore();
        var beforeExpiry = await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-b",
            now: originalLease.LeaseUntil.AddTicks(-1),
            leaseUntil: originalLease.LeaseUntil.AddMinutes(1)));
        var renewed = (await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "owner-b",
            now: originalLease.LeaseUntil,
            leaseUntil: originalLease.LeaseUntil.AddMinutes(1)))).Single();

        beforeExpiry.ShouldBeEmpty();
        renewed.Envelope.ShouldMatchEnvelope(envelope);
        renewed.OwnerId.ShouldBe("owner-b");
        renewed.Attempt.ShouldBe(2);
        renewed.LeaseToken.ShouldNotBe(originalLease.LeaseToken);
    }

    [Fact]
    public async Task Renewed_expiry_persists_exactly_after_reopen_without_a_schema_change()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "renewed-persistent");
        var renewedAt = DurableInputStoreConformanceData.Now.AddSeconds(1);
        var renewedUntil = DurableInputStoreConformanceData.Now.AddMinutes(2);
        await using (var writer = database.CreateStore())
        {
            await writer.EnqueueAsync(envelope);
            var lease = (await writer.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();
            var result = await writer.RenewLeaseAsync(new DurableInputLeaseRenewal(
                envelope.Key,
                lease.LeaseToken,
                renewedAt,
                renewedUntil));
            result.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }

        await using (var connection = await OpenAsync(database.DatabasePath))
        {
            await using var rowCommand = connection.CreateCommand();
            rowCommand.CommandText = """
                SELECT state, attempt, lease_until_utc_ticks
                FROM fluxflow_durable_inputs
                WHERE message_id = 'renewed-persistent';
                """;
            await using (var row = await rowCommand.ExecuteReaderAsync())
            {
                (await row.ReadAsync()).ShouldBeTrue();
                row.GetInt32(0).ShouldBe((int)DurableInputState.Leased);
                row.GetInt32(1).ShouldBe(1);
                row.GetInt64(2).ShouldBe(renewedUntil.UtcTicks);
                (await row.ReadAsync()).ShouldBeFalse();
            }

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText =
                "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;";
            Convert.ToInt32(await versionCommand.ExecuteScalarAsync()).ShouldBe(2);
        }

        await using var reader = database.CreateStore();
        var beforeExpiry = await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "reader",
            now: renewedUntil.AddTicks(-1),
            leaseUntil: renewedUntil.AddMinutes(1)));
        var recovered = (await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "reader",
            now: renewedUntil,
            leaseUntil: renewedUntil.AddMinutes(1)))).Single();

        beforeExpiry.ShouldBeEmpty();
        recovered.Envelope.ShouldMatchEnvelope(envelope);
        recovered.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task Delivered_and_dead_lettered_tombstones_and_failure_survive_store_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var deliveredEnvelope = DurableInputStoreConformanceData.Envelope(
            messageId: "delivered-persistent");
        var deadEnvelope = DurableInputStoreConformanceData.Envelope(
            messageId: "dead-persistent");
        var failure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.DeserializationFailed,
            "payload could not be restored");
        await using (var writer = database.CreateStore())
        {
            await writer.EnqueueAsync(deliveredEnvelope);
            await writer.EnqueueAsync(deadEnvelope);
            var leases = await writer.LeaseAsync(DurableInputStoreConformanceData.Request(maxCount: 2));
            var deliveredLease = leases.Single(lease => lease.Envelope.Key == deliveredEnvelope.Key);
            var deadLease = leases.Single(lease => lease.Envelope.Key == deadEnvelope.Key);
            (await writer.MarkDeliveredAsync(new DurableInputLeaseTransition(
                deliveredEnvelope.Key,
                deliveredLease.LeaseToken,
                DurableInputStoreConformanceData.Now.AddSeconds(1))))
                .Status.ShouldBe(DurableInputTransitionStatus.Applied);
            (await writer.DeadLetterAsync(new DurableInputDeadLetter(
                deadEnvelope.Key,
                deadLease.LeaseToken,
                DurableInputStoreConformanceData.Now.AddSeconds(1),
                failure)))
                .Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }

        await using (var connection = await OpenAsync(database.DatabasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT message_id, state, failure_kind, failure_description
                FROM fluxflow_durable_inputs
                ORDER BY message_id;
                """;
            await using var rows = await command.ExecuteReaderAsync();
            (await rows.ReadAsync()).ShouldBeTrue();
            rows.GetString(0).ShouldBe("dead-persistent");
            rows.GetInt32(1).ShouldBe((int)DurableInputState.DeadLettered);
            rows.GetInt32(2).ShouldBe((int)failure.Kind);
            rows.GetString(3).ShouldBe(failure.Description);
            (await rows.ReadAsync()).ShouldBeTrue();
            rows.GetString(0).ShouldBe("delivered-persistent");
            rows.GetInt32(1).ShouldBe((int)DurableInputState.Delivered);
            rows.IsDBNull(2).ShouldBeTrue();
            rows.IsDBNull(3).ShouldBeTrue();
            (await rows.ReadAsync()).ShouldBeFalse();
        }

        await using var reader = database.CreateStore();
        var leasesAfterReopen = await reader.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: DurableInputStoreConformanceData.Now.AddYears(1),
            leaseUntil: DurableInputStoreConformanceData.Now.AddYears(1).AddMinutes(1),
            maxCount: 2));
        var deliveredDuplicate = await reader.EnqueueAsync(deliveredEnvelope);
        var deadDuplicate = await reader.EnqueueAsync(deadEnvelope);

        leasesAfterReopen.ShouldBeEmpty();
        deliveredDuplicate.Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        deadDuplicate.Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
    }

    private static async ValueTask<SqliteConnection> OpenAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}
