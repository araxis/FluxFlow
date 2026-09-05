using FluxFlow.Engine.DurableInput.Tests;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputStatusStoreConformanceTests :
    DurableInputStatusStoreConformanceTests
{
    protected override async ValueTask<DurableInputStatusStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        await store.LeaseAsync(DurableInputStoreConformanceData.Request());
        return DurableInputStatusStoreTestContext.Create(
            store,
            store,
            async () =>
            {
                await store.DisposeAsync();
                database.Dispose();
            });
    }
}
