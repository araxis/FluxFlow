using Shouldly;
using Xunit;
using FluxFlow.Engine.DurableInput.Tests;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlDurableInputRetentionTests
{
    [Fact]
    public async Task Committed_retention_ignores_payload_content_persists_and_keeps_version_one_schema()
    {
        await using var database = await TSqlTestDatabase.CreateAsync();
        await using var store = database.CreateStore();
        var delivered = TSqlDurableInputTestSupport.ValueEnvelope("retention-persist-delivered");
        var deadLetter = TSqlDurableInputTestSupport.ValueEnvelope("retention-persist-dead-letter");
        var deliveredAt = TSqlDurableInputTestSupport.Now.AddHours(-2);
        var deadLetteredAt = TSqlDurableInputTestSupport.Now.AddHours(-1);

        var deliveredLease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(
            store,
            delivered,
            now: deliveredAt.AddMinutes(-1));
        (await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
            delivered.Key,
            deliveredLease.LeaseToken,
            deliveredAt))).Status.ShouldBe(DurableInputTransitionStatus.Applied);

        var deadLetterLease = await TSqlDurableInputTestSupport.EnqueueAndLeaseAsync(
            store,
            deadLetter,
            now: deadLetteredAt.AddMinutes(-1));
        (await store.DeadLetterAsync(new DurableInputDeadLetter(
            deadLetter.Key,
            deadLetterLease.LeaseToken,
            deadLetteredAt,
            DurableInputStoreConformanceData.Failure()))).Status
            .ShouldBe(DurableInputTransitionStatus.Applied);

        await database.ExecuteAsync(
            """
            UPDATE dbo.fluxflow_relational_inputs
            SET payload_json = N'{invalid-payload'
            WHERE message_id IN (N'retention-persist-delivered', N'retention-persist-dead-letter');
            """);

        (await store.PurgeDeliveredAsync(new(
            TSqlDurableInputTestSupport.Now,
            maxCount: 1))).DeletedCount.ShouldBe(1);
        (await store.PurgeDeadLettersAsync(new(
            TSqlDurableInputTestSupport.Now,
            maxCount: 1))).DeletedCount.ShouldBe(1);

        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fluxflow_relational_inputs;"))
            .ShouldBe(0);
        (await database.ScalarAsync<int>(
            "SELECT version FROM dbo.fluxflow_relational_input_schema WHERE singleton = 1;"))
            .ShouldBe(RelationalDurableInputSchema.CurrentVersion);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'fluxflow_relational_input%';"))
            .ShouldBe(2);
        (await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.objects WHERE name LIKE N'%retention%';"))
            .ShouldBe(0);

        await using var observer = database.CreateStore();
        var status = await observer.GetStatusAsync(new DurableInputStatusQuery(
            TSqlDurableInputTestSupport.Now.AddDays(1)));
        status.TotalCount.ShouldBe(0);
        status.DeliveredCount.ShouldBe(0);
        status.DeadLetteredCount.ShouldBe(0);
        (await observer.EnqueueAsync(delivered)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        (await observer.EnqueueAsync(deadLetter)).Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
    }
}
