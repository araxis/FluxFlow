using FluxFlow.Engine.DurableInput.Tests;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputStoreConformanceTests : DurableInputStoreConformanceTests
{
    protected override ValueTask<DurableInputStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableInputStoreTestContext.Create(
            store,
            async () =>
            {
                await store.DisposeAsync();
                database.Dispose();
            }));
    }
}
