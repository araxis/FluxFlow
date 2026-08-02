using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputRetentionTests
{
    [Fact]
    public async Task Parent_and_delivery_retention_is_physical_atomic_persistent_and_keeps_version_one_schema()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var completed = TSqlDurableOutputTestSupport.ValueEnvelope("retention-persist-completed");
        var deadLetter = TSqlDurableOutputTestSupport.ValueEnvelope("retention-persist-dead-letter");
        var completedAt = TSqlDurableOutputTestSupport.Now.AddHours(-2);
        var deadLetteredAt = TSqlDurableOutputTestSupport.Now.AddHours(-1);

        var completedLease = await TSqlDurableOutputTestSupport.CaptureAndLeaseAsync(
            store,
            completed,
            completedAt);
        (await store.CompleteAsync(new DurableOutputDeliveryTransition(
            completed.Key,
            completedLease.LeaseToken,
            completedAt))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        await TSqlDurableOutputTestSupport.CaptureAndDeadLetterAsync(
            store,
            deadLetter,
            deadLetteredAt,
            deadLetteredAt);

        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs;"))
            .ShouldBe(2);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_output_deliveries;"))
            .ShouldBe(2);
        await TSqlDurableOutputTestSupport.ExecuteAsync(
            database,
            """
            UPDATE dbo.fluxflow_relational_outputs
            SET payload_json = N'{invalid-payload'
            WHERE message_id IN (N'retention-persist-completed', N'retention-persist-dead-letter');
            """);

        (await store.PurgeCompletedAsync(new(
            TSqlDurableOutputTestSupport.Now,
            maxCount: 1))).DeletedCount.ShouldBe(1);
        (await store.PurgeDeadLettersAsync(new(
            TSqlDurableOutputTestSupport.Now,
            maxCount: 1))).DeletedCount.ShouldBe(1);

        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_outputs;"))
            .ShouldBe(0);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_output_deliveries;"))
            .ShouldBe(0);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT version FROM dbo.fluxflow_relational_output_schema WHERE singleton = 1;"))
            .ShouldBe(RelationalDurableOutputSchema.CurrentVersion);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_output%';"))
            .ShouldBe(3);
        (await TSqlDurableOutputTestSupport.ScalarAsync<int>(
            database,
            "SELECT COUNT(*) FROM sys.objects WHERE name LIKE N'%retention%';"))
            .ShouldBe(0);

        await using var observer = database.CreateStore();
        var status = await observer.GetStatusAsync(new DurableOutputStatusQuery(
            TSqlDurableOutputTestSupport.Now.AddDays(1)));
        status.CapturedCount.ShouldBe(0);
        status.CompletedCount.ShouldBe(0);
        status.DeadLetteredCount.ShouldBe(0);
        (await observer.EnqueueAsync(completed)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        (await observer.EnqueueAsync(deadLetter)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
    }
}
