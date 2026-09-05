using FluxFlow.Engine.DurableOutput.Tests;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputStoreConformanceTests : DurableOutputStoreConformanceTests
{
    protected override ValueTask<DurableOutputStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableOutputStoreTestContext.Create(
            store,
            (key, cancellationToken) => SqlFileDurableOutputTestDatabase.ReadOutputAsync(
                database.DatabasePath,
                key,
                cancellationToken),
            async () =>
            {
                await store.DisposeAsync();
                database.Dispose();
            }));
    }
}
