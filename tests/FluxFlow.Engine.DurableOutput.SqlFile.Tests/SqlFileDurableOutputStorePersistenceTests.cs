using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Engine.DurableOutput.Tests;
using FluxFlow.Nodes;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputStorePersistenceTests
{
    [Fact]
    public async Task Complete_value_envelope_round_trips_exactly_after_store_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var expected = SqlFileDurableOutputTestData.CompleteValueEnvelope();
        await using (var writer = database.CreateStore())
        {
            (await writer.EnqueueAsync(expected)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        }

        var persisted = await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            expected.Key);
        await using var reader = database.CreateStore();
        var duplicate = await reader.EnqueueAsync(DurableOutputStoreConformanceData.Copy(
            expected,
            capturedAt: expected.CapturedAt.AddYears(1)));

        persisted.ShouldNotBeNull().ShouldMatchExactly(expected);
        duplicate.Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
    }

    [Fact]
    public async Task Complete_error_envelope_round_trips_exactly_after_store_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var expected = SqlFileDurableOutputTestData.CompleteErrorEnvelope();
        await using (var writer = database.CreateStore())
        {
            (await writer.EnqueueAsync(expected)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        }

        var persisted = await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            expected.Key);
        await using var reader = database.CreateStore();
        var conflict = await reader.EnqueueAsync(CopyError(
            expected,
            new FlowError(
                expected.Error!.Code,
                "changed",
                expected.Error.Category,
                expected.Error.IsTransient,
                expected.Error.Details)));

        persisted.ShouldNotBeNull().ShouldMatchExactly(expected);
        conflict.Status.ShouldBe(DurableOutputEnqueueStatus.Conflict);
        (await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            expected.Key)).ShouldNotBeNull().ShouldMatchExactly(expected);
    }

    [Fact]
    public async Task Null_optional_ids_empty_headers_and_json_null_value_round_trip_exactly()
    {
        using var database = TemporarySqliteDatabase.Create();
        var expected = new DurableOutputEnvelope(
            DurableOutputStoreConformanceData.Output,
            "nullable-v1",
            isError: false,
            JsonSerializer.SerializeToElement<object?>(null),
            error: null,
            new MessageId("nullable-fields"),
            new TraceId("trace-nullable"),
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(13)),
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.FromHours(-11)),
            correlationId: null,
            causationId: null,
            new Dictionary<string, string>(),
            schemaVersion: 3);
        await using var store = database.CreateStore();

        (await store.EnqueueAsync(expected)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var persisted = await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            expected.Key);

        persisted.ShouldNotBeNull().ShouldMatchExactly(expected);
        persisted.CorrelationId.ShouldBeNull();
        persisted.CausationId.ShouldBeNull();
        persisted.Headers.ShouldBeEmpty();
        persisted.Payload.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Stored_row_preserves_exact_ticks_offsets_and_canonical_header_json()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = SqlFileDurableOutputTestData.CompleteValueEnvelope("raw-row");
        await using var store = database.CreateStore();
        await store.EnqueueAsync(envelope);

        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT message_timestamp_utc_ticks,
                   message_timestamp_offset_minutes,
                   captured_at_utc_ticks,
                   captured_at_offset_minutes,
                   headers_json
            FROM fluxflow_durable_outputs
            WHERE message_id = 'raw-row';
            """;
        await using var row = await command.ExecuteReaderAsync();

        (await row.ReadAsync()).ShouldBeTrue();
        row.GetInt64(0).ShouldBe(envelope.Timestamp.UtcTicks);
        row.GetInt32(1).ShouldBe((int)envelope.Timestamp.Offset.TotalMinutes);
        row.GetInt64(2).ShouldBe(envelope.CapturedAt.UtcTicks);
        row.GetInt32(3).ShouldBe((int)envelope.CapturedAt.Offset.TotalMinutes);
        row.GetString(4).ShouldBe("{\"Tenant\":\"North\",\"source\":\"provider-test-\\u2713\"}");
        (await row.ReadAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Semantic_payload_header_order_and_recapture_are_already_exists_after_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var original = DurableOutputStoreConformanceData.Envelope(
            messageId: "semantic-value",
            payload: JsonSerializer.SerializeToElement(new
            {
                customer = new { id = 7, name = "Ada" },
                values = new[] { 1, 2, 3 }
            }),
            headers: new Dictionary<string, string>
            {
                ["alpha"] = "1",
                ["beta"] = "2"
            });
        var equivalent = DurableOutputStoreConformanceData.Copy(
            original,
            payload: Parse("{\"values\":[1,2,3],\"customer\":{\"name\":\"Ada\",\"id\":7}}"),
            capturedAt: original.CapturedAt.AddDays(1),
            headers: new Dictionary<string, string>
            {
                ["beta"] = "2",
                ["alpha"] = "1"
            });
        await using (var writer = database.CreateStore())
            (await writer.EnqueueAsync(original)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        await using var reader = database.CreateStore(createDatabase: false);
        var duplicate = await reader.EnqueueAsync(equivalent);

        duplicate.Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            original.Key)).ShouldNotBeNull().ShouldMatchExactly(original);
    }

    [Fact]
    public async Task Semantic_error_details_are_already_exists_after_reopen()
    {
        using var database = TemporarySqliteDatabase.Create();
        var original = SqlFileDurableOutputTestData.CompleteErrorEnvelope("semantic-error");
        var originalError = original.Error!;
        var equivalent = CopyError(
            original,
            new FlowError(
                originalError.Code,
                originalError.Message,
                originalError.Category,
                originalError.IsTransient,
                Parse("{\"reasons\":[\"missing\",\"påkrævet\"],\"field\":\"customerId\"}")));
        await using (var writer = database.CreateStore())
            (await writer.EnqueueAsync(original)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        await using var reader = database.CreateStore(createDatabase: false);
        var duplicate = await reader.EnqueueAsync(equivalent);

        duplicate.Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            original.Key)).ShouldNotBeNull().ShouldMatchExactly(original);
    }

    public static TheoryData<ErrorMutation> ErrorMutations =>
    [
        ErrorMutation.Code,
        ErrorMutation.Message,
        ErrorMutation.Category,
        ErrorMutation.IsTransient,
        ErrorMutation.Details
    ];

    [Theory]
    [MemberData(nameof(ErrorMutations))]
    public async Task Every_flow_error_field_group_conflicts_without_overwriting_winner(
        ErrorMutation mutation)
    {
        using var database = TemporarySqliteDatabase.Create();
        var original = SqlFileDurableOutputTestData.CompleteErrorEnvelope($"error-{mutation}");
        var error = original.Error!;
        var changedError = new FlowError(
            mutation == ErrorMutation.Code ? "changed.code" : error.Code,
            mutation == ErrorMutation.Message ? "Changed message" : error.Message,
            mutation == ErrorMutation.Category ? "changed-category" : error.Category,
            mutation == ErrorMutation.IsTransient ? !error.IsTransient : error.IsTransient,
            mutation == ErrorMutation.Details
                ? JsonSerializer.SerializeToElement(new { field = "changed" })
                : error.Details);
        await using var store = database.CreateStore();

        var first = await store.EnqueueAsync(original);
        var conflict = await store.EnqueueAsync(CopyError(original, changedError));
        var retained = await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            original.Key);

        first.Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        conflict.Status.ShouldBe(DurableOutputEnqueueStatus.Conflict);
        retained.ShouldNotBeNull().ShouldMatchExactly(original);
    }

    [Fact]
    public async Task Successful_enqueue_owns_commit_even_if_callers_token_is_canceled_after_return()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableOutputStoreConformanceData.Envelope(messageId: "committed");
        using var cancellation = new CancellationTokenSource();
        await using (var writer = database.CreateStore())
        {
            (await writer.EnqueueAsync(envelope, cancellation.Token))
                .Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
            cancellation.Cancel();
        }

        var retained = await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            envelope.Key);

        retained.ShouldNotBeNull().ShouldMatchExactly(envelope);
    }

    private static DurableOutputEnvelope CopyError(
        DurableOutputEnvelope original,
        FlowError error)
        => new(
            original.Address,
            original.ContractName,
            isError: true,
            original.Payload,
            error,
            original.MessageId,
            original.TraceId,
            original.Timestamp,
            original.CapturedAt,
            original.CorrelationId,
            original.CausationId,
            original.Headers,
            original.SchemaVersion);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public enum ErrorMutation
    {
        Code,
        Message,
        Category,
        IsTransient,
        Details
    }
}
