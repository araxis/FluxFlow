using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Engine.DurableOutput.Tests;
using FluxFlow.Nodes;
using Shouldly;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

internal static class SqlFileDurableOutputTestData
{
    public static DurableOutputEnvelope CompleteValueEnvelope(
        string messageId = "complete-value")
        => new(
            DurableOutputStoreConformanceData.Output,
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
            new DateTimeOffset(2026, 7, 29, 8, 2, 3, 456, TimeSpan.FromHours(-3)).AddTicks(7890),
            new CorrelationId("correlation-complete-value"),
            new MessageId("cause-complete-value"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tenant"] = "North",
                ["source"] = "provider-test-✓"
            },
            schemaVersion: 7);

    public static DurableOutputEnvelope CompleteErrorEnvelope(
        string messageId = "complete-error")
        => new(
            DurableOutputStoreConformanceData.SecondaryOutput,
            "order.failure-v2",
            isError: true,
            JsonSerializer.SerializeToElement<object?>(null),
            new FlowError(
                "order.invalid",
                "The order is invalid — ग्राहक.",
                "validation",
                isTransient: true,
                details: JsonSerializer.SerializeToElement(new
                {
                    field = "customerId",
                    reasons = new[] { "missing", "påkrævet" }
                })),
            new MessageId(messageId),
            new TraceId("trace-complete-error"),
            new DateTimeOffset(2026, 7, 29, 7, 1, 2, 345, TimeSpan.FromHours(-4)).AddTicks(1234),
            new DateTimeOffset(2026, 7, 29, 7, 2, 3, 456, TimeSpan.FromHours(5.5)).AddTicks(2345),
            new CorrelationId("correlation-complete-error"),
            new MessageId("cause-complete-error"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tenant"] = "South",
                ["sensitive-name"] = "hemlig-✓"
            },
            schemaVersion: 9);

    public static void ShouldMatchExactly(
        this DurableOutputEnvelope actual,
        DurableOutputEnvelope expected)
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
        actual.CapturedAt.ShouldBe(expected.CapturedAt);
        actual.CapturedAt.Offset.ShouldBe(expected.CapturedAt.Offset);
        actual.CapturedAt.Ticks.ShouldBe(expected.CapturedAt.Ticks);
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
}
