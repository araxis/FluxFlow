using FluxFlow.Engine.DurableInput.Tests;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputDeadLetterStoreConformanceTests :
    DurableInputDeadLetterStoreConformanceTests
{
    protected override ValueTask<DurableInputDeadLetterStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableInputDeadLetterStoreTestContext.Create(
            store,
            store,
            async () =>
            {
                await store.DisposeAsync();
                database.Dispose();
            }));
    }
}
