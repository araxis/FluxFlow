using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.DurableInput.Tests;
using FluxFlow.Nodes;
using Shouldly;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

internal static class TSqlDurableInputTestSupport
{
    internal static readonly DateTimeOffset Now =
        new(2026, 8, 1, 14, 0, 0, TimeSpan.Zero);

    internal static DurableInputEnvelope ValueEnvelope(string messageId = "value")
        => new(
            DurableInputStoreConformanceData.Input,
            "order.created-v3",
            isError: false,
            JsonSerializer.SerializeToElement(new
            {
                orderId = 17,
                customer = "Göteborg 客户",
                lines = new[]
                {
                    new { sku = "Å-1", quantity = 2 },
                    new { sku = "B-2", quantity = 1 }
                }
            }),
            error: null,
            new MessageId(messageId),
            new TraceId("trace-complete-value"),
            new DateTimeOffset(2026, 7, 29, 8, 1, 2, 345, TimeSpan.FromHours(2)).AddTicks(6789),
            new DateTimeOffset(2026, 7, 29, 8, 2, 3, 456, TimeSpan.FromHours(2)).AddTicks(7890),
            new CorrelationId("correlation-complete-value"),
            new MessageId("cause-complete-value"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tenant"] = "North",
                ["source"] = "provider-test-✓"
            },
            schemaVersion: 7);

    internal static DurableInputEnvelope ErrorEnvelope(string messageId = "error")
        => new(
            DurableInputStoreConformanceData.SecondaryInput,
            "order.failure-v2",
            isError: true,
            JsonSerializer.SerializeToElement<object?>(null),
            new FlowError(
                "order.invalid",
                "The order is invalid — ग्राहक.",
                "validation",
                isTransient: true,
                JsonSerializer.SerializeToElement(new
                {
                    field = "customerId",
                    reasons = new[] { "missing", "påkrævet" }
                })),
            new MessageId(messageId),
            new TraceId("trace-complete-error"),
            new DateTimeOffset(2026, 7, 29, 7, 1, 2, 345, TimeSpan.FromHours(-4)).AddTicks(1234),
            new DateTimeOffset(2026, 7, 29, 7, 2, 3, 456, TimeSpan.FromHours(-4)).AddTicks(2345),
            new CorrelationId("correlation-complete-error"),
            new MessageId("cause-complete-error"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tenant"] = "South",
                ["sensitive-name"] = "hemlig-✓"
            },
            schemaVersion: 9);

    internal static void ShouldMatchExactly(
        this DurableInputEnvelope actual,
        DurableInputEnvelope expected)
    {
        actual.Key.ShouldBe(expected.Key);
        actual.Address.ShouldBe(expected.Address);
        actual.ContractName.ShouldBe(expected.ContractName);
        actual.IsError.ShouldBe(expected.IsError);
        actual.Payload.GetRawText().ShouldBe(expected.Payload.GetRawText());
        actual.MessageId.ShouldBe(expected.MessageId);
        actual.TraceId.ShouldBe(expected.TraceId);
        actual.Timestamp.ShouldBe(expected.Timestamp);
        actual.Timestamp.Offset.ShouldBe(expected.Timestamp.Offset);
        actual.Timestamp.Ticks.ShouldBe(expected.Timestamp.Ticks);
        actual.EnqueuedAt.ShouldBe(expected.EnqueuedAt);
        actual.EnqueuedAt.Offset.ShouldBe(expected.EnqueuedAt.Offset);
        actual.EnqueuedAt.Ticks.ShouldBe(expected.EnqueuedAt.Ticks);
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
            actual.Error.Details?.GetRawText().ShouldBe(expected.Error.Details?.GetRawText());
        }

        actual.HasSameContent(expected).ShouldBeTrue();
    }

    internal static async ValueTask<DurableInputLease> EnqueueAndLeaseAsync(
        TSqlDurableInputStore store,
        DurableInputEnvelope envelope,
        string owner = "worker",
        DateTimeOffset? now = null)
    {
        var leasedAt = now ?? Now;
        (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        var leases = await store.LeaseAsync(new(
            owner,
            leasedAt,
            leasedAt.AddMinutes(5),
            1));
        return leases.ShouldHaveSingleItem();
    }

    internal static async ValueTask ExecuteAsync(
        this TSqlTestDatabase database,
        string sql)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    internal static async ValueTask<T> ScalarAsync<T>(
        this TSqlTestDatabase database,
        string sql)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
            throw new InvalidDataException("The scalar query did not return a value.");
        return (T)Convert.ChangeType(
            value,
            typeof(T),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
