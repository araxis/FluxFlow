using System.Data;
using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.SqlClient;
using Shouldly;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

internal static class TSqlDurableOutputTestSupport
{
    internal static readonly DateTimeOffset Now =
        new(2026, 8, 1, 13, 0, 0, TimeSpan.FromHours(2));

    internal static DurableOutputEnvelope ValueEnvelope(
        string messageId,
        DateTimeOffset? capturedAt = null)
        => DurableOutputStoreConformanceData.Envelope(messageId, capturedAt: capturedAt);

    internal static DurableOutputEnvelope ErrorEnvelope(
        string messageId,
        DateTimeOffset? capturedAt = null)
        => DurableOutputStoreConformanceData.ErrorEnvelope(messageId, capturedAt);

    internal static DurableOutputDeliveryLeaseRequest Request(
        DateTimeOffset now,
        string owner = "relational-worker",
        TimeSpan? duration = null)
        => DurableOutputStoreConformanceData.DeliveryRequest(
            now,
            owner,
            duration ?? TimeSpan.FromMinutes(1));

    internal static DurableOutputDeliveryDeadLetter DeadLetter(
        DurableOutputKey key,
        Guid token,
        DateTimeOffset at)
        => DurableOutputStoreConformanceData.DeadLetter(key, token, at);

    internal static async ValueTask<DurableOutputDeliveryLease> CaptureAndLeaseAsync(
        TSqlDurableOutputStore store,
        DurableOutputEnvelope envelope,
        DateTimeOffset leaseAt,
        string owner = "relational-worker")
    {
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        var lease = await store.TryLeaseAsync(Request(leaseAt, owner));
        lease.ShouldNotBeNull();
        return lease;
    }

    internal static async ValueTask<DurableOutputDeadLetterDetails> CaptureAndDeadLetterAsync(
        TSqlDurableOutputStore store,
        DurableOutputEnvelope envelope,
        DateTimeOffset leaseAt,
        DateTimeOffset deadLetteredAt)
    {
        var lease = await CaptureAndLeaseAsync(store, envelope, leaseAt);
        (await store.DeadLetterAsync(DeadLetter(
            envelope.Key,
            lease.LeaseToken,
            deadLetteredAt))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        return (await store.GetAsync(envelope.Key)).ShouldNotBeNull();
    }

    internal static async ValueTask ExecuteAsync(
        TSqlTestDatabase database,
        string sql,
        Action<SqlCommand>? configure = null)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 10;
        configure?.Invoke(command);
        await command.ExecuteNonQueryAsync();
    }

    internal static async ValueTask<T> ScalarAsync<T>(
        TSqlTestDatabase database,
        string sql,
        Action<SqlCommand>? configure = null)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 10;
        configure?.Invoke(command);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
            throw new InvalidDataException("The scalar database query returned no value.");

        return (T)(Convert.ChangeType(
            value,
            typeof(T),
            System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException("The scalar database value could not be converted."));
    }

    internal static async ValueTask<IReadOnlyList<string>> ReadStringsAsync(
        TSqlTestDatabase database,
        string sql,
        Action<SqlCommand>? configure = null)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 10;
        configure?.Invoke(command);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(string.Join(
                "|",
                Enumerable.Range(0, reader.FieldCount)
                    .Select(index => reader.IsDBNull(index)
                        ? "<null>"
                        : Convert.ToString(
                            reader.GetValue(index),
                            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)));
        }

        return values;
    }

    internal static SqlParameter AddKeyParameter(
        SqlCommand command,
        string name,
        string value,
        int size)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.NVarChar, size);
        parameter.Value = value;
        return parameter;
    }

    internal static void ShouldMatchExactly(
        this DurableOutputEnvelope actual,
        DurableOutputEnvelope expected)
    {
        actual.HasSameContent(expected).ShouldBeTrue();
        actual.Key.ShouldBe(expected.Key);
        actual.ContractName.ShouldBe(expected.ContractName);
        actual.IsError.ShouldBe(expected.IsError);
        actual.Payload.GetRawText().ShouldBe(expected.Payload.GetRawText());
        actual.TraceId.ShouldBe(expected.TraceId);
        ShouldHaveExactTime(actual.Timestamp, expected.Timestamp);
        ShouldHaveExactTime(actual.CapturedAt, expected.CapturedAt);
        actual.CorrelationId.ShouldBe(expected.CorrelationId);
        actual.CausationId.ShouldBe(expected.CausationId);
        actual.Headers.ShouldBe(expected.Headers);
        actual.SchemaVersion.ShouldBe(expected.SchemaVersion);
        if (expected.Error is null)
        {
            actual.Error.ShouldBeNull();
        }
        else
        {
            actual.Error.ShouldNotBeNull();
            actual.Error.Code.ShouldBe(expected.Error.Code);
            actual.Error.Message.ShouldBe(expected.Error.Message);
            actual.Error.Category.ShouldBe(expected.Error.Category);
            actual.Error.IsTransient.ShouldBe(expected.Error.IsTransient);
            actual.Error.Details.HasValue.ShouldBe(expected.Error.Details.HasValue);
            if (expected.Error.Details is { } expectedDetails)
            {
                var actualDetails = actual.Error.Details.ShouldNotBeNull();
                System.Text.Json.JsonElement.DeepEquals(actualDetails, expectedDetails)
                    .ShouldBeTrue();
                actualDetails.GetRawText().ShouldBe(expectedDetails.GetRawText());
            }
        }
    }

    internal static void ShouldHaveExactTime(DateTimeOffset actual, DateTimeOffset expected)
    {
        actual.UtcTicks.ShouldBe(expected.UtcTicks);
        actual.Offset.ShouldBe(expected.Offset);
    }
}
