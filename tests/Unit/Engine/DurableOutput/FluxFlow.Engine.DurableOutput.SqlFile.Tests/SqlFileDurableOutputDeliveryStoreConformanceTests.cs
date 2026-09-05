using FluxFlow.Engine.DurableOutput.Tests;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputDeliveryStoreConformanceTests :
    DurableOutputDeliveryStoreConformanceTests
{
    protected override ValueTask<DurableOutputDeliveryStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableOutputDeliveryStoreTestContext.Create(
            store,
            store,
            async () =>
            {
                try
                {
                    await store.DisposeAsync();
                }
                finally
                {
                    database.Dispose();
                }
            }));
    }
}
