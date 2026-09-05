using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputPersistenceTests
{
    [Fact]
    public async Task Capture_and_lease_persist_exact_state_encoding()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("encoding-value");
        var leaseAt = TSqlDurableOutputTestSupport.Now;
        var lease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(
            store,
            envelope,
            leaseAt,
            "encoding-worker");

        var outputRows = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT application_address, message_id, contract_name, envelope_schema_version,
                   CONVERT(int, is_error), payload_json, error_code, error_message,
                   error_category, error_is_transient, error_details_json, trace_id,
                   correlation_id, causation_id, message_timestamp_utc_ticks,
                   message_timestamp_offset_minutes, captured_at_utc_ticks,
                   captured_at_offset_minutes, headers_json
            FROM dbo.fluxflow_relational_outputs;
            """);
        outputRows.ShouldBe([
            string.Join(
                "|",
                envelope.Key.Address.ToString(),
                envelope.Key.MessageId.ToString(),
                envelope.ContractName,
                envelope.SchemaVersion,
                0,
                envelope.Payload.GetRawText(),
                "<null>", "<null>", "<null>", "<null>", "<null>",
                envelope.TraceId.ToString(),
                envelope.CorrelationId?.ToString() ?? "<null>",
                envelope.CausationId?.ToString() ?? "<null>",
                envelope.Timestamp.UtcTicks,
                (short)envelope.Timestamp.Offset.TotalMinutes,
                envelope.CapturedAt.UtcTicks,
                (short)envelope.CapturedAt.Offset.TotalMinutes,
                "{\"source\":\"orders\",\"tenant\":\"north\"}")
        ]);

        var deliveryRows = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT application_address, message_id, state, next_attempt_utc_ticks,
                   next_attempt_offset_minutes, CONVERT(nvarchar(36), lease_token), lease_owner,
                   leased_at_utc_ticks, leased_at_offset_minutes, lease_until_utc_ticks,
                   lease_until_offset_minutes, attempt, delivered_at_utc_ticks,
                   delivered_at_offset_minutes, dead_letter_reason, dead_lettered_at_utc_ticks,
                   dead_lettered_at_offset_minutes, dead_letter_generation
            FROM dbo.fluxflow_relational_output_deliveries;
            """);
        deliveryRows.ShouldBe([
            string.Join(
                "|",
                envelope.Key.Address.ToString(),
                envelope.Key.MessageId.ToString(),
                2,
                envelope.CapturedAt.UtcTicks,
                (short)envelope.CapturedAt.Offset.TotalMinutes,
                lease.LeaseToken.ToString("D").ToUpperInvariant(),
                lease.OwnerId,
                lease.LeasedAt.UtcTicks,
                (short)lease.LeasedAt.Offset.TotalMinutes,
                lease.LeaseUntil.UtcTicks,
                (short)lease.LeaseUntil.Offset.TotalMinutes,
                1,
                "<null>", "<null>", "<null>", "<null>", "<null>",
                0)
        ]);
    }

    [Fact]
    public async Task Value_envelope_survives_store_disposal_and_reopen_exactly()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("reopen-value");
        await using (var writer = database.CreateStore())
            (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        await using var reader = database.CreateStore();
        var persisted = await reader.ReadAsync(envelope.Key);
        persisted.ShouldNotBeNull();
        persisted.ShouldMatchExactly(envelope);
    }

    [Fact]
    public async Task Error_envelope_survives_store_disposal_and_reopen_exactly()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableOutputTestSupport.ErrorEnvelope("reopen-error");
        await using (var writer = database.CreateStore())
            (await writer.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        await using var reader = database.CreateStore();
        var persisted = await reader.ReadAsync(envelope.Key);
        persisted.ShouldNotBeNull();
        persisted.ShouldMatchExactly(envelope);
    }

    [Fact]
    public async Task Completion_tombstone_survives_reopen_with_exact_timestamp()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("reopen-completed");
        var completedAt = TSqlDurableOutputTestSupport.Now.AddSeconds(10);

        await using (var writer = database.CreateStore())
        {
            var lease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(
                writer,
                envelope,
                TSqlDurableOutputTestSupport.Now);
            (await writer.CompleteAsync(new(
                envelope.Key,
                lease.LeaseToken,
                completedAt))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        }

        await using var reader = database.CreateStore();
        (await reader.TryLeaseAsync(TSqlDurableOutputTestSupport.Request(
            completedAt.AddDays(1)))).ShouldBeNull();
        (await reader.ReadAsync(envelope.Key)).ShouldNotBeNull().ShouldMatchExactly(envelope);
        var rows = await TSqlDurableOutputTestSupport.ReadStringsAsync(
            database,
            """
            SELECT state, delivered_at_utc_ticks, delivered_at_offset_minutes, attempt,
                   lease_token, lease_owner, leased_at_utc_ticks, lease_until_utc_ticks
            FROM dbo.fluxflow_relational_output_deliveries;
            """);
        rows.ShouldBe([$"3|{completedAt.UtcTicks}|{(short)completedAt.Offset.TotalMinutes}|1|<null>|<null>|<null>|<null>"]);
    }

    [Fact]
    public async Task Dead_letter_generation_and_replay_schedule_survive_reopen()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        var envelope = TSqlDurableOutputTestSupport.ErrorEnvelope("reopen-dead-letter");
        var deadLetteredAt = TSqlDurableOutputTestSupport.Now.AddSeconds(10);
        DurableOutputDeadLetterDetails before;

        await using (var writer = database.CreateStore())
        {
            before = await TSqlDurableOutputTestSupport.CaptureAndDeadLetterAsync(
                writer,
                envelope,
                TSqlDurableOutputTestSupport.Now,
                deadLetteredAt);
        }

        await using (var replayer = database.CreateStore())
        {
            var reopened = await replayer.GetAsync(envelope.Key);
            reopened.ShouldNotBeNull();
            reopened.Envelope.ShouldMatchExactly(envelope);
            reopened.Attempt.ShouldBe(1);
            reopened.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
            reopened.Generation.ShouldBe(1);
            TSqlDurableOutputTestSupport.ShouldHaveExactTime(reopened.DeadLetteredAt, deadLetteredAt);

            var replayedAt = deadLetteredAt.AddSeconds(5);
            var nextAttemptAt = deadLetteredAt.AddMinutes(2);
            (await replayer.ReplayAsync(new(
                envelope.Key,
                before.Generation,
                replayedAt,
                nextAttemptAt))).Status.ShouldBe(DurableOutputReplayStatus.Replayed);
        }

        await using var reader = database.CreateStore();
        (await reader.GetAsync(envelope.Key)).ShouldBeNull();
        (await reader.TryLeaseAsync(TSqlDurableOutputTestSupport.Request(
            deadLetteredAt.AddMinutes(1)))).ShouldBeNull();
        var replayLease = await reader.TryLeaseAsync(TSqlDurableOutputTestSupport.Request(
            deadLetteredAt.AddMinutes(2)));
        replayLease.ShouldNotBeNull();
        replayLease.Envelope.ShouldMatchExactly(envelope);
        replayLease.Attempt.ShouldBe(1);
        (await TSqlDurableOutputTestSupport.ScalarAsync<long>(
            database,
            "SELECT dead_letter_generation FROM dbo.fluxflow_relational_output_deliveries;"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Binary_key_ordering_ignores_database_default_collation()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var upper = TSqlDurableOutputTestSupport.ValueEnvelope("A-message");
        var lower = TSqlDurableOutputTestSupport.ValueEnvelope("a-message");
        (await store.EnqueueAsync(lower)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await store.EnqueueAsync(upper)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        var databaseCollation = await TSqlDurableOutputTestSupport.ScalarAsync<string>(
            database,
            "SELECT collation_name FROM sys.databases WHERE database_id = DB_ID();");
        StringComparer.OrdinalIgnoreCase.Equals(databaseCollation, "Latin1_General_100_BIN2")
            .ShouldBeFalse();

        var first = await store.TryLeaseAsync(TSqlDurableOutputTestSupport.Request(
            TSqlDurableOutputTestSupport.Now,
            "binary-1"));
        first.ShouldNotBeNull();
        first.Envelope.Key.ShouldBe(upper.Key);
        var second = await store.TryLeaseAsync(TSqlDurableOutputTestSupport.Request(
            TSqlDurableOutputTestSupport.Now,
            "binary-2"));
        second.ShouldNotBeNull();
        second.Envelope.Key.ShouldBe(lower.Key);

        (await store.DeadLetterAsync(TSqlDurableOutputTestSupport.DeadLetter(
            upper.Key,
            first.LeaseToken,
            TSqlDurableOutputTestSupport.Now.AddSeconds(1)))).IsApplied.ShouldBeTrue();
        (await store.DeadLetterAsync(TSqlDurableOutputTestSupport.DeadLetter(
            lower.Key,
            second.LeaseToken,
            TSqlDurableOutputTestSupport.Now.AddSeconds(1)))).IsApplied.ShouldBeTrue();
        var page = await store.ListAsync(new(pageSize: 2));
        page.Items.Select(static item => item.Key).ShouldBe([upper.Key, lower.Key]);
    }

    [Fact]
    public async Task Dead_letter_list_projects_metadata_without_reading_payload_headers_or_error_details()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var envelope = TSqlDurableOutputTestSupport.ErrorEnvelope("metadata-only");
        var deadLetteredAt = TSqlDurableOutputTestSupport.Now.AddSeconds(10);
        var details = await TSqlDurableOutputTestSupport.CaptureAndDeadLetterAsync(
            store,
            envelope,
            TSqlDurableOutputTestSupport.Now,
            deadLetteredAt);

        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            """
            ALTER TABLE dbo.fluxflow_relational_outputs
                NOCHECK CONSTRAINT ck_fluxflow_relational_outputs_shape;

            UPDATE dbo.fluxflow_relational_outputs
            SET payload_json = N'{invalid-payload',
                headers_json = N'{invalid-headers',
                error_details_json = N'{invalid-details'
            WHERE application_address = @address AND message_id = @message;
            """,
            command => AddKey(command, envelope));

        var page = await store.ListAsync(new(pageSize: 1));
        page.Items.Count.ShouldBe(1);
        var summary = page.Items[0];
        summary.Key.ShouldBe(envelope.Key);
        summary.ContractName.ShouldBe(envelope.ContractName);
        summary.EnvelopeSchemaVersion.ShouldBe(envelope.SchemaVersion);
        summary.IsError.ShouldBeTrue();
        summary.Attempt.ShouldBe(1);
        summary.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        summary.Generation.ShouldBe(details.Generation);
        TSqlDurableOutputTestSupport.ShouldHaveExactTime(summary.CapturedAt, envelope.CapturedAt);
        TSqlDurableOutputTestSupport.ShouldHaveExactTime(summary.DeadLetteredAt, deadLetteredAt);

        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => store.GetAsync(envelope.Key).AsTask());
        exception.Message.ShouldContain("payload JSON");
    }

    private static void AddKey(SqlCommand command, DurableOutputEnvelope envelope)
    {
        TSqlDurableOutputTestSupport.AddKeyParameter(
            command,
            "@address",
            envelope.Key.Address.ToString(),
            300);
        TSqlDurableOutputTestSupport.AddKeyParameter(
            command,
            "@message",
            envelope.Key.MessageId.ToString(),
            128);
    }
}
