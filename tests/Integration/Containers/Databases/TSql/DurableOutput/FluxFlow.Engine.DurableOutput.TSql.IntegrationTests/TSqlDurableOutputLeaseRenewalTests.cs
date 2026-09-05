using System.Data;
using System.Diagnostics;
using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputLeaseRenewalTests
{
    [Fact]
    public async Task Renewal_persists_exact_expiry_across_stores_without_hydration_or_schema_change()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var owner = database.CreateStore();
        await using var renewer = database.CreateStore();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("tsql-renew-persistence");
        var lease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(
            owner,
            envelope,
            TSqlDurableOutputTestSupport.Now,
            "persistent-owner");
        var renewedAt = TSqlDurableOutputTestSupport.Now.AddSeconds(1);
        var renewedUntil = TSqlDurableOutputTestSupport.Now.AddMinutes(3)
            .ToOffset(TimeSpan.FromHours(-4));
        var rowBefore = await ReadNonExpiryDeliveryRowAsync(database, envelope.Key);
        var schemaBefore = await ReadSchemaFingerprintAsync(database);
        await CorruptCaptureJsonAsync(database, envelope.Key);

        var result = await renewer.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            renewedAt,
            renewedUntil));

        result.ShouldBe(new DurableOutputDeliveryTransitionResult(
            envelope.Key,
            DurableOutputDeliveryTransitionStatus.Applied));
        (await owner.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            renewedAt.AddSeconds(1),
            renewedUntil))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await ReadNonExpiryDeliveryRowAsync(database, envelope.Key)).ShouldBe(rowBefore);
        (await ReadExpiryAsync(database, envelope.Key)).ShouldBe(
            $"{renewedUntil.UtcTicks}|{(int)renewedUntil.Offset.TotalMinutes}");
        (await ReadSchemaFingerprintAsync(database)).ShouldBe(schemaBefore);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT version FROM dbo.fluxflow_relational_output_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.objects WHERE lower(name) LIKE N'%renew%';"))
            .ShouldBe(0);
    }

    [Theory]
    [InlineData(RenewalSettlement.Complete)]
    [InlineData(RenewalSettlement.Retry)]
    [InlineData(RenewalSettlement.DeadLetter)]
    public async Task Renewal_racing_settlement_across_stores_has_only_valid_atomic_outcomes(
        RenewalSettlement settlement)
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var owner = database.CreateStore();
        await using var renewalStore = database.CreateStore();
        await using var settlementStore = database.CreateStore();
        var now = TSqlDurableOutputTestSupport.Now;
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope($"tsql-renew-race-{settlement}");
        var lease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(owner, envelope, now);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalTask = Task.Run(async () =>
        {
            await start.Task;
            return await renewalStore.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
                envelope.Key,
                lease.LeaseToken,
                now.AddSeconds(1),
                now.AddMinutes(3)));
        });
        var settlementTask = Task.Run(async () =>
        {
            await start.Task;
            return await SettleAsync();
        });

        start.TrySetResult();
        var renewalResult = await renewalTask;
        var settlementResult = await settlementTask;

        settlementResult.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        new[]
        {
            DurableOutputDeliveryTransitionStatus.Applied,
            DurableOutputDeliveryTransitionStatus.InvalidState
        }.ShouldContain(renewalResult.Status);
        var persisted = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT state,
                   attempt,
                   next_attempt_utc_ticks,
                   lease_token,
                   lease_owner,
                   lease_until_utc_ticks,
                   delivered_at_utc_ticks,
                   dead_letter_reason,
                   dead_lettered_at_utc_ticks,
                   dead_letter_generation
            FROM dbo.fluxflow_relational_output_deliveries;
            """);
        persisted.ShouldBe([settlement switch
        {
            RenewalSettlement.Complete => $"3|1|{envelope.CapturedAt.UtcTicks}|<null>|<null>|<null>|{now.AddSeconds(1).UtcTicks}|<null>|<null>|0",
            RenewalSettlement.Retry => $"1|1|{now.AddSeconds(5).UtcTicks}|<null>|<null>|<null>|<null>|<null>|<null>|0",
            RenewalSettlement.DeadLetter => $"4|1|{envelope.CapturedAt.UtcTicks}|<null>|<null>|<null>|<null>|1|{now.AddSeconds(1).UtcTicks}|1",
            _ => throw new ArgumentOutOfRangeException(nameof(settlement), settlement, null)
        }]);

        ValueTask<DurableOutputDeliveryTransitionResult> SettleAsync()
            => settlement switch
            {
                RenewalSettlement.Complete => settlementStore.CompleteAsync(
                    new DurableOutputDeliveryTransition(
                        envelope.Key,
                        lease.LeaseToken,
                        now.AddSeconds(1))),
                RenewalSettlement.Retry => settlementStore.RetryAsync(
                    new DurableOutputDeliveryRetry(
                        envelope.Key,
                        lease.LeaseToken,
                        now.AddSeconds(1),
                        now.AddSeconds(5))),
                RenewalSettlement.DeadLetter => settlementStore.DeadLetterAsync(
                    TSqlDurableOutputTestSupport.DeadLetter(
                        envelope.Key,
                        lease.LeaseToken,
                        now.AddSeconds(1))),
                _ => throw new ArgumentOutOfRangeException(nameof(settlement), settlement, null)
            };
    }

    [Fact]
    public async Task Renewal_racing_expired_release_across_stores_preserves_one_current_owner()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var owner = database.CreateStore();
        await using var renewalStore = database.CreateStore();
        await using var competitor = database.CreateStore();
        var now = TSqlDurableOutputTestSupport.Now;
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("tsql-renew-release-race");
        (await owner.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var original = (await owner.TryLeaseAsync(
            TSqlDurableOutputTestSupport.Request(now, "original-owner", TimeSpan.FromSeconds(1))))
            .ShouldNotBeNull();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalTask = Task.Run(async () =>
        {
            await start.Task;
            return await renewalStore.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
                envelope.Key,
                original.LeaseToken,
                original.LeaseUntil.AddTicks(-1),
                original.LeaseUntil.AddMinutes(1)));
        });
        var leaseTask = Task.Run(async () =>
        {
            await start.Task;
            return await competitor.TryLeaseAsync(
                TSqlDurableOutputTestSupport.Request(
                    original.LeaseUntil,
                    "competing-owner",
                    TimeSpan.FromMinutes(1)));
        });

        start.TrySetResult();
        var renewal = await renewalTask;
        var competing = await leaseTask;

        if (renewal.Status == DurableOutputDeliveryTransitionStatus.Applied)
        {
            competing.ShouldBeNull();
            (await owner.CompleteAsync(new DurableOutputDeliveryTransition(
                envelope.Key,
                original.LeaseToken,
                original.LeaseUntil))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
            (await ReadStateAndAttemptAsync(database)).ShouldBe("3|1");
        }
        else
        {
            renewal.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
            competing.ShouldNotBeNull();
            competing.Attempt.ShouldBe(2);
            competing.OwnerId.ShouldBe("competing-owner");
            competing.LeaseToken.ShouldNotBe(original.LeaseToken);
            (await competitor.CompleteAsync(new DurableOutputDeliveryTransition(
                envelope.Key,
                competing.LeaseToken,
                competing.LeasedAt.AddTicks(1)))).Status
                .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
            (await ReadStateAndAttemptAsync(database)).ShouldBe("3|2");
        }
    }

    [Fact]
    public async Task Cancelled_blocked_renewal_preserves_exact_row_and_same_store_recovers()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = database.ConnectionString,
            CommandTimeout = TimeSpan.FromSeconds(10)
        });
        var now = TSqlDurableOutputTestSupport.Now;
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("tsql-renew-cancel-blocked");
        var lease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(store, envelope, now);
        var renewal = new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            now.AddSeconds(1),
            now.AddMinutes(3));
        await using var lockConnection = await database.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = """
                UPDATE dbo.fluxflow_relational_output_deliveries
                SET attempt = attempt
                WHERE application_address = @address AND message_id = @messageId;
                """;
            AddKey(lockCommand, envelope.Key);
            (await lockCommand.ExecuteNonQueryAsync()).ShouldBe(1);
        }
        var before = await ReadExpiryAsync(lockConnection, transaction, envelope.Key);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            store.RenewLeaseAsync(renewal, cancellation.Token).AsTask());
        stopwatch.Stop();

        exception.CancellationToken.ShouldBe(cancellation.Token);
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        (await ReadExpiryAsync(lockConnection, transaction, envelope.Key)).ShouldBe(before);
        await transaction.RollbackAsync();

        (await store.RenewLeaseAsync(renewal)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await ReadExpiryAsync(database, envelope.Key)).ShouldBe(
            $"{renewal.LeaseUntil.UtcTicks}|{(int)renewal.LeaseUntil.Offset.TotalMinutes}");
    }

    private static ValueTask<string> ReadNonExpiryDeliveryRowAsync(
        TSqlTestDatabase database,
        DurableOutputKey key)
        => ReadSingleAsync(
            database,
            """
            SELECT application_address,
                   message_id,
                   state,
                   next_attempt_utc_ticks,
                   next_attempt_offset_minutes,
                   CONVERT(nvarchar(36), lease_token),
                   lease_owner,
                   leased_at_utc_ticks,
                   leased_at_offset_minutes,
                   attempt,
                   delivered_at_utc_ticks,
                   delivered_at_offset_minutes,
                   dead_letter_reason,
                   dead_lettered_at_utc_ticks,
                   dead_lettered_at_offset_minutes,
                   dead_letter_generation
            FROM dbo.fluxflow_relational_output_deliveries
            WHERE application_address = @address AND message_id = @messageId;
            """,
            command => AddKey(command, key));

    private static ValueTask<string> ReadExpiryAsync(
        TSqlTestDatabase database,
        DurableOutputKey key)
        => ReadSingleAsync(
            database,
            """
            SELECT lease_until_utc_ticks, lease_until_offset_minutes
            FROM dbo.fluxflow_relational_output_deliveries
            WHERE application_address = @address AND message_id = @messageId;
            """,
            command => AddKey(command, key));

    private static async ValueTask<string> ReadExpiryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableOutputKey key)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lease_until_utc_ticks, lease_until_offset_minutes
            FROM dbo.fluxflow_relational_output_deliveries
            WHERE application_address = @address AND message_id = @messageId;
            """;
        AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        var value = $"{reader.GetInt64(0)}|{reader.GetInt16(1)}";
        (await reader.ReadAsync()).ShouldBeFalse();
        return value;
    }

    private static ValueTask<string> ReadStateAndAttemptAsync(TSqlTestDatabase database)
        => ReadSingleAsync(
            database,
            "SELECT state, attempt FROM dbo.fluxflow_relational_output_deliveries;");

    private static async ValueTask<string> ReadSingleAsync(
        TSqlTestDatabase database,
        string sql,
        Action<SqlCommand>? configure = null)
    {
        var values = await TSqlDurableOutputTestSupport.ReadStringsAsync(database, sql, configure);
        return values.ShouldHaveSingleItem();
    }

    private static async ValueTask<IReadOnlyList<string>> ReadSchemaFingerprintAsync(
        TSqlTestDatabase database)
    {
        var objects = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT o.type_desc, o.name
            FROM sys.objects AS o
            WHERE o.name LIKE N'fluxflow_relational_output%'
               OR o.name LIKE N'pk_fluxflow_relational_output%'
               OR o.name LIKE N'fk_fluxflow_relational_output%'
               OR o.name LIKE N'ck_fluxflow_relational_output%'
            ORDER BY o.type_desc, o.name;
            """);
        var columns = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT t.name,
                   c.column_id,
                   c.name,
                   ty.name,
                   c.max_length,
                   c.precision,
                   c.scale,
                   c.is_nullable
            FROM sys.tables AS t
            INNER JOIN sys.columns AS c ON c.object_id = t.object_id
            INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE t.name LIKE N'fluxflow_relational_output%'
            ORDER BY t.name, c.column_id;
            """);
        var indexes = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT t.name, i.name, i.is_unique
            FROM sys.tables AS t
            INNER JOIN sys.indexes AS i ON i.object_id = t.object_id
            WHERE t.name LIKE N'fluxflow_relational_output%'
              AND i.name IS NOT NULL
            ORDER BY t.name, i.name;
            """);
        return objects.Select(static value => "object|" + value)
            .Concat(columns.Select(static value => "column|" + value))
            .Concat(indexes.Select(static value => "index|" + value))
            .ToArray();
    }

    private static ValueTask CorruptCaptureJsonAsync(
        TSqlTestDatabase database,
        DurableOutputKey key)
        => TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            """
            UPDATE dbo.fluxflow_relational_outputs
            SET payload_json = N'not-json', headers_json = N'also-not-json'
            WHERE application_address = @address AND message_id = @messageId;
            """,
            command => AddKey(command, key));

    private static void AddKey(SqlCommand command, DurableOutputKey key)
    {
        TSqlDurableOutputTestSupport.AddKeyParameter(
            command,
            "@address",
            key.Address.Value,
            300);
        TSqlDurableOutputTestSupport.AddKeyParameter(
            command,
            "@messageId",
            key.MessageId.Value,
            128);
    }

    public enum RenewalSettlement
    {
        Complete,
        Retry,
        DeadLetter
    }
}
