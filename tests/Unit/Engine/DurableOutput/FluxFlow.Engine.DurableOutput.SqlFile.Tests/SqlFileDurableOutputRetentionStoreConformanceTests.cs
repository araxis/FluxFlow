using FluxFlow.Engine.DurableOutput.Tests;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputRetentionStoreConformanceTests :
    DurableOutputRetentionStoreConformanceTests
{
    protected override ValueTask<DurableOutputRetentionStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableOutputRetentionStoreTestContext.Create(
            store,
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
