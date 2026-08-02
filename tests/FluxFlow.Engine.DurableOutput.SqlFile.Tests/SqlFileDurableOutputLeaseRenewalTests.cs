using System.Diagnostics;
using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputLeaseRenewalTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 14, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task Renewal_persists_exact_expiry_without_hydrating_payload_and_changes_only_expiry_columns()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-renew-exact-row");
        var renewedAt = Now.AddSeconds(1);
        var renewedUntil = Now.AddMinutes(2).ToOffset(TimeSpan.FromHours(-3));
        DurableOutputDeliveryLease lease;
        DeliveryRow before;
        IReadOnlyList<string> schemaBefore;
        await using (var first = database.CreateStore())
        {
            await first.EnqueueAsync(envelope);
            lease = (await first.TryLeaseAsync(Request(Now, "original-owner")))
                .ShouldNotBeNull();
            await using (var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
                database.DatabasePath))
            {
                schemaBefore = await ReadSchemaAsync(connection);
                before = await ReadDeliveryRowAsync(connection, envelope.Key);
                await CorruptCaptureJsonAsync(connection, envelope.Key);
            }

            var result = await first.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
                envelope.Key,
                lease.LeaseToken,
                renewedAt,
                renewedUntil));

            result.ShouldBe(new DurableOutputDeliveryTransitionResult(
                envelope.Key,
                DurableOutputDeliveryTransitionStatus.Applied));
        }

        await using var reopened = database.CreateStore(createDatabase: false, createDirectory: false);
        (await reopened.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            renewedAt.AddSeconds(1),
            renewedUntil))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        await using var verification = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        var after = await ReadDeliveryRowAsync(verification, envelope.Key);
        after.ShouldBe(before with
        {
            LeaseUntilUtcTicks = renewedUntil.UtcTicks,
            LeaseUntilOffsetMinutes = (int)renewedUntil.Offset.TotalMinutes
        });
        after.LeaseToken.ShouldBe(lease.LeaseToken.ToString("N"));
        after.LeaseOwner.ShouldBe("original-owner");
        after.State.ShouldBe(2);
        after.Attempt.ShouldBe(1);
        (await ReadSchemaAsync(verification)).ShouldBe(schemaBefore);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            verification,
            "SELECT version FROM fluxflow_durable_output_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            verification,
            "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            verification,
            "SELECT COUNT(*) FROM sqlite_schema WHERE lower(name) LIKE '%renew%';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Locked_renewal_fails_bounded_preserves_exact_row_and_same_store_recovers()
    {
        using var database = TemporarySqliteDatabase.Create();
        var timeout = TimeSpan.FromMilliseconds(150);
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-renew-lock");
        await using var store = database.CreateStore(busyTimeout: timeout);
        await store.EnqueueAsync(envelope);
        var lease = (await store.TryLeaseAsync(Request(Now))).ShouldNotBeNull();
        var renewal = new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            Now.AddSeconds(1),
            Now.AddMinutes(2));
        await using var lockConnection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        var before = await ReadDeliveryRowAsync(lockConnection, envelope.Key);
        await using var writeLock = lockConnection.BeginTransaction(deferred: false);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.RenewLeaseAsync(renewal).AsTask());
        stopwatch.Stop();

        exception.Message.ShouldContain("delivery lease renewal");
        exception.Message.ShouldContain("configured busy timeout");
        exception.InnerException.ShouldBeOfType<SqliteException>();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        (await ReadDeliveryRowAsync(lockConnection, envelope.Key)).ShouldBe(before);
        await writeLock.RollbackAsync();

        (await store.RenewLeaseAsync(renewal)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        await using var verification = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        var recovered = await ReadDeliveryRowAsync(verification, envelope.Key);
        recovered.ShouldBe(before with
        {
            LeaseUntilUtcTicks = renewal.LeaseUntil.UtcTicks,
            LeaseUntilOffsetMinutes = (int)renewal.LeaseUntil.Offset.TotalMinutes
        });
    }

    [Fact]
    public async Task Precancelled_first_renewal_is_io_free_and_disposed_store_rejects_renewal()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nested = Path.Combine(database.DirectoryPath, "renewal-not-created");
        var path = Path.Combine(nested, "delivery.db");
        var store = new SqlFileDurableOutputStore(new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true
        });
        var envelope = DurableOutputStoreConformanceData.Envelope("sqlite-renew-lifecycle");
        var renewal = new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            Guid.Parse("eb92649c-d675-4601-8122-5fac7ad74ed1"),
            Now,
            Now.AddSeconds(30));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            store.RenewLeaseAsync(renewal, cancellation.Token).AsTask());

        Directory.Exists(nested).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
        await store.DisposeAsync();
        await store.DisposeAsync();
        var exception = await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.RenewLeaseAsync(renewal).AsTask());
        exception.ObjectName.ShouldContain(nameof(SqlFileDurableOutputStore));
        File.Exists(path).ShouldBeFalse();
    }

    private static DurableOutputDeliveryLeaseRequest Request(
        DateTimeOffset now,
        string owner = "worker-1")
        => new(owner, now, now.AddSeconds(30));

    private static async ValueTask<DeliveryRow> ReadDeliveryRowAsync(
        SqliteConnection connection,
        DurableOutputKey key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT application_address,
                   message_id,
                   state,
                   next_attempt_utc_ticks,
                   next_attempt_offset_minutes,
                   lease_token,
                   lease_owner,
                   leased_at_utc_ticks,
                   leased_at_offset_minutes,
                   lease_until_utc_ticks,
                   lease_until_offset_minutes,
                   attempt,
                   delivered_at_utc_ticks,
                   delivered_at_offset_minutes,
                   dead_letter_reason,
                   dead_lettered_at_utc_ticks,
                   dead_lettered_at_offset_minutes,
                   dead_letter_generation
            FROM fluxflow_durable_output_deliveries
            WHERE application_address = $address AND message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$address", key.Address.Value);
        command.Parameters.AddWithValue("$messageId", key.MessageId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        var row = new DeliveryRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10),
            reader.GetInt64(11),
            reader.IsDBNull(12) ? null : reader.GetInt64(12),
            reader.IsDBNull(13) ? null : reader.GetInt64(13),
            reader.IsDBNull(14) ? null : reader.GetInt64(14),
            reader.IsDBNull(15) ? null : reader.GetInt64(15),
            reader.IsDBNull(16) ? null : reader.GetInt64(16),
            reader.GetInt64(17));
        (await reader.ReadAsync()).ShouldBeFalse();
        return row;
    }

    private static async ValueTask CorruptCaptureJsonAsync(
        SqliteConnection connection,
        DurableOutputKey key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE fluxflow_durable_outputs
            SET payload_json = 'not-json', headers_json = 'also-not-json'
            WHERE application_address = $address AND message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$address", key.Address.Value);
        command.Parameters.AddWithValue("$messageId", key.MessageId.Value);
        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }

    private static ValueTask<IReadOnlyList<string>> ReadSchemaAsync(SqliteConnection connection)
        => SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT type || ':' || name || ':' || COALESCE(sql, '')
            FROM sqlite_schema
            WHERE name LIKE 'fluxflow_durable_output%'
               OR name LIKE 'ix_fluxflow_durable_output%'
            ORDER BY type, name;
            """);

    private sealed record DeliveryRow(
        string ApplicationAddress,
        string MessageId,
        long State,
        long NextAttemptUtcTicks,
        long NextAttemptOffsetMinutes,
        string? LeaseToken,
        string? LeaseOwner,
        long? LeasedAtUtcTicks,
        long? LeasedAtOffsetMinutes,
        long? LeaseUntilUtcTicks,
        long? LeaseUntilOffsetMinutes,
        long Attempt,
        long? DeliveredAtUtcTicks,
        long? DeliveredAtOffsetMinutes,
        long? DeadLetterReason,
        long? DeadLetteredAtUtcTicks,
        long? DeadLetteredAtOffsetMinutes,
        long DeadLetterGeneration);
}
