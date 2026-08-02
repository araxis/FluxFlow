using FluxFlow.Engine.DurableOutput.Tests;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputStatusStoreConformanceTests :
    DurableOutputStatusStoreConformanceTests
{
    protected override async ValueTask<DurableOutputStatusStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        await store.TryLeaseAsync(DurableOutputStoreConformanceData.DeliveryRequest(
            DurableOutputStoreConformanceData.DeliveryNow));
        return DurableOutputStatusStoreTestContext.Create(
            store,
            store,
            store,
            async () =>
            {
                await store.DisposeAsync();
                database.Dispose();
            });
    }
}
