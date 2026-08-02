using System.Diagnostics;
using System.Text.Json;
using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputDeadLetterOperationsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_round_trips_complete_value_and_error_envelopes(bool isError)
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = isError
            ? SqlFileDurableInputTestData.CompleteErrorEnvelope("dead-letter-error")
            : SqlFileDurableInputTestData.CompleteValueEnvelope("dead-letter-value");
        var failure = DurableInputStoreConformanceData.Failure(
            DurableInputFailureKind.DeserializationFailed,
            "complete envelope could not be restored");
        var deadLetteredAt = envelope.EnqueuedAt.AddMinutes(1);
        await using (var writer = database.CreateStore())
        {
            await DeadLetterAsync(writer, envelope, deadLetteredAt, failure);
        }

        await using var store = database.CreateStore();

        var details = await store.GetAsync(envelope.Key);

        details.ShouldNotBeNull();
        details.Envelope.ShouldMatchEnvelope(envelope);
        details.Attempt.ShouldBe(1);
        details.Failure.ShouldBe(failure);
        details.DeadLetteredAt.ShouldBe(deadLetteredAt);
        details.Generation.ShouldBe(1);
        if (isError)
        {
            details.Envelope.Error.ShouldNotBeNull();
            details.Envelope.Error.Message.ShouldBe("The order is invalid — ग्राहक.");
            details.Envelope.Error.Details.ShouldNotBeNull();
            details.Envelope.Error.Details.Value
                .GetProperty("reasons")[1]
                .GetString()
                .ShouldBe("påkrævet");
            details.Envelope.Headers["sensitive-name"].ShouldBe("hemlig-✓");
        }
        else
        {
            details.Envelope.Payload.GetProperty("customer").GetString()
                .ShouldBe("Göteborg 客户");
            details.Envelope.Headers["source"].ShouldBe("provider-test-✓");
        }
    }

    [Fact]
    public async Task Concurrent_replay_has_one_winner_one_not_dead_lettered_result_and_one_pending_record()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope("concurrent-replay");
        await using (var writer = database.CreateStore())
        {
            await DeadLetterAsync(
                writer,
                envelope,
                DurableInputStoreConformanceData.Now,
                DurableInputStoreConformanceData.Failure());
        }

        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var replay = new DurableInputReplay(
            envelope.Key,
            expectedGeneration: 1,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            DurableInputStoreConformanceData.Now.AddMinutes(1));

        var results = await Task.WhenAll(
            first.ReplayAsync(replay).AsTask(),
            second.ReplayAsync(replay).AsTask());
        var leases = await first.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "replay-verifier",
            now: replay.NextAttemptAt,
            leaseUntil: replay.NextAttemptAt.AddMinutes(1),
            maxCount: 2));

        results.Select(static result => result.Status).ShouldBe([
            DurableInputReplayStatus.Replayed,
            DurableInputReplayStatus.NotDeadLettered
        ], ignoreOrder: true);
        results.Count(static result => result.IsReplayed).ShouldBe(1);
        var lease = leases.ShouldHaveSingleItem();
        lease.Envelope.ShouldMatchEnvelope(envelope);
        lease.Attempt.ShouldBe(1);
        (await second.GetAsync(envelope.Key)).ShouldBeNull();
    }

    [Fact]
    public async Task External_write_lock_honors_busy_timeout_then_replay_recovers_after_release()
    {
        using var database = TemporarySqliteDatabase.Create();
        var timeout = TimeSpan.FromMilliseconds(150);
        var envelope = DurableInputStoreConformanceData.Envelope("locked-replay");
        await using var store = database.CreateStore(busyTimeout: timeout);
        await DeadLetterAsync(
            store,
            envelope,
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Failure());
        var replay = new DurableInputReplay(
            envelope.Key,
            expectedGeneration: 1,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            DurableInputStoreConformanceData.Now.AddMinutes(1));
        await using var lockConnection = await OpenAsync(database.DatabasePath);
        await using var writeLock = lockConnection.BeginTransaction(deferred: false);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => store.ReplayAsync(replay).AsTask());
        stopwatch.Stop();

        exception.Message.ShouldContain("dead-letter replay");
        exception.Message.ShouldContain("configured busy timeout");
        exception.Message.ShouldContain(timeout.ToString());
        exception.InnerException.ShouldBeOfType<SqliteException>();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        await writeLock.RollbackAsync();

        (await store.ReplayAsync(replay)).Status.ShouldBe(DurableInputReplayStatus.Replayed);
        (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: replay.NextAttemptAt,
            leaseUntil: replay.NextAttemptAt.AddMinutes(1))))
            .ShouldHaveSingleItem().Envelope.Key.ShouldBe(envelope.Key);
    }

    [Fact]
    public async Task Replay_resets_every_operational_column_and_preserves_generation_and_envelope_data()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope(
            "replay-columns",
            "preserved-replay-payload",
            headers: new Dictionary<string, string> { ["preserved"] = "header-value" });
        await using var store = database.CreateStore();
        await DeadLetterAsync(
            store,
            envelope,
            DurableInputStoreConformanceData.Now,
            DurableInputStoreConformanceData.Failure(
                DurableInputFailureKind.InvalidEnvelope,
                "cleared replay failure"));
        var replay = new DurableInputReplay(
            envelope.Key,
            expectedGeneration: 1,
            DurableInputStoreConformanceData.Now.AddSeconds(1),
            DurableInputStoreConformanceData.Now.AddMinutes(3));

        (await store.ReplayAsync(replay)).Status.ShouldBe(DurableInputReplayStatus.Replayed);

        await using var connection = await OpenAsync(database.DatabasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT state,
                   attempt,
                   next_attempt_utc_ticks,
                   lease_owner,
                   lease_token,
                   leased_at_utc_ticks,
                   lease_until_utc_ticks,
                   failure_kind,
                   failure_description,
                   delivered_at_utc_ticks,
                   dead_lettered_at_utc_ticks,
                   dead_letter_generation,
                   payload_json,
                   headers_json
            FROM fluxflow_durable_inputs
            WHERE application_address = $address AND message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$address", envelope.Address.Value);
        command.Parameters.AddWithValue("$messageId", envelope.MessageId.Value);
        await using var row = await command.ExecuteReaderAsync();
        (await row.ReadAsync()).ShouldBeTrue();
        row.GetInt32(0).ShouldBe((int)DurableInputState.Pending);
        row.GetInt32(1).ShouldBe(0);
        row.GetInt64(2).ShouldBe(replay.NextAttemptAt.UtcDateTime.Ticks);
        Enumerable.Range(3, 8).ShouldAllBe(ordinal => row.IsDBNull(ordinal));
        row.GetInt64(11).ShouldBe(1);
        row.GetString(12).ShouldBe(envelope.Payload.GetRawText());
        using var headers = JsonDocument.Parse(row.GetString(13));
        headers.RootElement.GetProperty("preserved").GetString().ShouldBe("header-value");
    }

    [Theory]
    [InlineData(DeadLetterFirstOperation.List)]
    [InlineData(DeadLetterFirstOperation.Get)]
    [InlineData(DeadLetterFirstOperation.Replay)]
    public async Task Precancelled_first_operation_creates_neither_directory_nor_database(
        DeadLetterFirstOperation operation)
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "cancelled-dead-letters");
        var path = Path.Combine(nestedDirectory, "durable-input.db");
        await using var store = new SqlFileDurableInputStore(new SqlFileDurableInputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true
        });
        var key = DurableInputStoreConformanceData.Envelope("cancelled-first-use").Key;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(InvokeAsync);

        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();

        async Task InvokeAsync()
        {
            switch (operation)
            {
                case DeadLetterFirstOperation.List:
                    await store.ListAsync(new DurableInputDeadLetterQuery(), cancellation.Token);
                    break;
                case DeadLetterFirstOperation.Get:
                    await store.GetAsync(key, cancellation.Token);
                    break;
                case DeadLetterFirstOperation.Replay:
                    await store.ReplayAsync(
                        new DurableInputReplay(
                            key,
                            1,
                            DurableInputStoreConformanceData.Now,
                            DurableInputStoreConformanceData.Now),
                        cancellation.Token);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }

    [Theory]
    [InlineData("attempt", "0", "attempt")]
    [InlineData("is_error", "2", "is_error")]
    [InlineData("failure_kind", "999", "failure kind")]
    [InlineData("dead_lettered_at_utc_ticks", "9223372036854775807", "dead-letter timestamp")]
    [InlineData("dead_letter_generation", "0", "dead-letter generation")]
    public async Task Corrupt_dead_letter_metadata_is_rejected_with_key_and_field_context(
        string column,
        string value,
        string field)
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope($"corrupt-{column}");
        await using (var writer = database.CreateStore())
        {
            await DeadLetterAsync(
                writer,
                envelope,
                DurableInputStoreConformanceData.Now,
                DurableInputStoreConformanceData.Failure());
        }

        await using (var connection = await OpenAsync(database.DatabasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "PRAGMA ignore_check_constraints = ON; " +
                $"UPDATE fluxflow_durable_inputs SET {column} = {value} " +
                "WHERE message_id = $messageId;";
            command.Parameters.AddWithValue("$messageId", envelope.MessageId.Value);
            (await command.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        await using var reader = database.CreateStore();
        var listException = await Should.ThrowAsync<InvalidDataException>(
            () => reader.ListAsync(new DurableInputDeadLetterQuery()).AsTask());
        var getException = await Should.ThrowAsync<InvalidDataException>(
            () => reader.GetAsync(envelope.Key).AsTask());

        listException.Message.ShouldContain(envelope.Address.Value);
        listException.Message.ShouldContain(envelope.MessageId.Value);
        listException.Message.ShouldContain(field);
        getException.Message.ShouldContain(envelope.Address.Value);
        getException.Message.ShouldContain(envelope.MessageId.Value);
        getException.Message.ShouldContain(field);
    }

    [Fact]
    public async Task Corrupt_dead_letter_address_is_rejected_as_an_invalid_key()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope("corrupt-address");
        await using (var writer = database.CreateStore())
        {
            await DeadLetterAsync(
                writer,
                envelope,
                DurableInputStoreConformanceData.Now,
                DurableInputStoreConformanceData.Failure());
        }

        await using (var connection = await OpenAsync(database.DatabasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE fluxflow_durable_inputs
                SET application_address = 'not-an-application-address'
                WHERE message_id = $messageId;
                """;
            command.Parameters.AddWithValue("$messageId", envelope.MessageId.Value);
            (await command.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        await using var reader = database.CreateStore();
        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => reader.ListAsync(new DurableInputDeadLetterQuery()).AsTask());

        exception.Message.ShouldContain("dead-letter row");
        exception.Message.ShouldContain("invalid key");
        exception.InnerException.ShouldBeOfType<FormatException>();
    }

    private static async ValueTask DeadLetterAsync(
        SqlFileDurableInputStore store,
        DurableInputEnvelope envelope,
        DateTimeOffset deadLetteredAt,
        DurableInputFailure failure)
    {
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var lease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            now: envelope.EnqueuedAt,
            leaseUntil: deadLetteredAt.AddMinutes(1)))).Single();
        (await store.DeadLetterAsync(new DurableInputDeadLetter(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt,
            failure))).Status.ShouldBe(DurableInputTransitionStatus.Applied);
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

    public enum DeadLetterFirstOperation
    {
        List,
        Get,
        Replay
    }
}
