using FluxFlow.Engine.DurableInput.Tests;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputRetentionStoreConformanceTests :
    DurableInputRetentionStoreConformanceTests
{
    protected override ValueTask<DurableInputRetentionStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableInputRetentionStoreTestContext.Create(
            store,
            store,
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
