using FluxFlow.Engine.DurableOutput.Tests;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputDeadLetterStoreConformanceTests :
    DurableOutputDeadLetterStoreConformanceTests
{
    protected override ValueTask<DurableOutputDeadLetterStoreTestContext> CreateStoreAsync()
    {
        var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        return ValueTask.FromResult(DurableOutputDeadLetterStoreTestContext.Create(
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
