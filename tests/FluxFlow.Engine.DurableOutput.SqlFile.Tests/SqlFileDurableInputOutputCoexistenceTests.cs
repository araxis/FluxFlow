using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Engine.DurableInput.SqlFile;
using FluxFlow.Engine.DurableOutput.Tests;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableInputOutputCoexistenceTests
{
    [Theory]
    [InlineData(InitializationOrder.OutputFirst)]
    [InlineData(InitializationOrder.InputFirst)]
    public async Task Input_and_output_providers_coexist_and_reopen_in_one_sqlite_file(
        InitializationOrder order)
    {
        using var database = TemporarySqliteDatabase.Create("shared.db");
        var output = SqlFileDurableOutputTestData.CompleteValueEnvelope("shared-message");
        var input = CreateInputEnvelope("shared-message");
        await using (var outputWriter = database.CreateStore())
        await using (var inputWriter = CreateInputStore(database.DatabasePath))
        {
            if (order == InitializationOrder.OutputFirst)
            {
                (await outputWriter.EnqueueAsync(output))
                    .Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
                (await inputWriter.EnqueueAsync(input))
                    .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
            }
            else
            {
                (await inputWriter.EnqueueAsync(input))
                    .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
                (await outputWriter.EnqueueAsync(output))
                    .Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
            }
        }

        await using (var deliveryWriter = database.CreateStore())
        {
            var deliveryNow = output.CapturedAt.AddMinutes(1);
            var deliveryLease = (await deliveryWriter.TryLeaseAsync(
                new DurableOutputDeliveryLeaseRequest(
                    "coexistence-delivery",
                    deliveryNow,
                    deliveryNow.AddMinutes(1)))).ShouldNotBeNull();
            deliveryLease.Envelope.ShouldMatchExactly(output);
            (await deliveryWriter.CompleteAsync(new DurableOutputDeliveryTransition(
                output.Key,
                deliveryLease.LeaseToken,
                deliveryNow.AddSeconds(1)))).Status
                .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        }

        await using (var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath))
        {
            var objects = await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
                connection,
                """
                SELECT name
                FROM sqlite_schema
                WHERE name LIKE 'fluxflow_durable_input%'
                   OR name LIKE 'fluxflow_durable_output%'
                   OR name LIKE 'ix_fluxflow_durable_input%'
                   OR name LIKE 'ix_fluxflow_durable_output%'
                ORDER BY name;
                """);
            objects.ShouldBe([
                "fluxflow_durable_input_schema",
                "fluxflow_durable_inputs",
                "fluxflow_durable_output_deliveries",
                "fluxflow_durable_output_delivery_schema",
                "fluxflow_durable_output_schema",
                "fluxflow_durable_outputs",
                "ix_fluxflow_durable_inputs_dead_lettered",
                "ix_fluxflow_durable_inputs_lease_expiry",
                "ix_fluxflow_durable_inputs_pending_due",
                "ix_fluxflow_durable_output_deliveries_dead_lettered",
                "ix_fluxflow_durable_output_deliveries_eligibility"
            ]);
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                connection,
                "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
                .ShouldBe(2);
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                connection,
                "SELECT version FROM fluxflow_durable_output_schema WHERE singleton = 1;"))
                .ShouldBe(1);
            (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
                connection,
                "SELECT version FROM fluxflow_durable_output_delivery_schema WHERE singleton = 1;"))
                .ShouldBe(2);
        }

        await using var outputReader = database.CreateStore();
        await using var inputReader = CreateInputStore(database.DatabasePath);
        (await outputReader.EnqueueAsync(output))
            .Status.ShouldBe(DurableOutputEnqueueStatus.AlreadyExists);
        (await outputReader.TryLeaseAsync(new DurableOutputDeliveryLeaseRequest(
            "coexistence-reopen",
            output.CapturedAt.AddDays(1),
            output.CapturedAt.AddDays(1).AddMinutes(1)))).ShouldBeNull();
        (await inputReader.EnqueueAsync(input))
            .Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        var lease = (await inputReader.LeaseAsync(new DurableInputLeaseRequest(
            "coexistence-reader",
            input.EnqueuedAt.AddMinutes(1),
            input.EnqueuedAt.AddMinutes(2),
            maxCount: 1))).Single();
        var persistedOutput = await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            output.Key);

        lease.Envelope.Key.ShouldBe(input.Key);
        lease.Envelope.Payload.GetRawText().ShouldBe(input.Payload.GetRawText());
        persistedOutput.ShouldNotBeNull().ShouldMatchExactly(output);
    }

    private static SqlFileDurableInputStore CreateInputStore(string path)
        => new(new SqlFileDurableInputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true
        });

    private static DurableInputEnvelope CreateInputEnvelope(string messageId)
        => new(
            ApplicationAddress.WorkflowPort("Orders", "Consumer", "Input"),
            "order.created-v1",
            isError: false,
            JsonSerializer.SerializeToElement(new { orderId = 17, source = "shared" }),
            error: null,
            new MessageId(messageId),
            new TraceId("trace-shared-input"),
            new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 7, 30, 8, 1, 0, TimeSpan.Zero),
            new CorrelationId("correlation-shared"),
            new MessageId("cause-shared"),
            new Dictionary<string, string> { ["source"] = "coexistence" },
            schemaVersion: 1);

    public enum InitializationOrder
    {
        OutputFirst,
        InputFirst
    }
}
