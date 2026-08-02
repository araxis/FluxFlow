using FluxFlow.Engine.DurableInput.Tests;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputLeaseRenewalStoreConformanceTests :
    DurableInputLeaseRenewalStoreConformanceTests
{
    protected override ValueTask<DurableInputLeaseRenewalStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableInputLeaseRenewalStoreTestContext.Create(
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
