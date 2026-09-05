using FluxFlow.Engine.DurableOutput.Tests;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlDurableOutputStoreConformanceTests :
    DurableOutputStoreConformanceTests
{
    protected override async ValueTask<DurableOutputStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableOutputStoreTestContext.Create(
            store,
            store.ReadAsync,
            database.DisposeAsync);
    }
}

public sealed class TSqlDurableOutputDeliveryStoreConformanceTests :
    DurableOutputDeliveryStoreConformanceTests
{
    protected override async ValueTask<DurableOutputDeliveryStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableOutputDeliveryStoreTestContext.Create(
            store,
            store,
            database.DisposeAsync);
    }
}

public sealed class TSqlDurableOutputDeadLetterStoreConformanceTests :
    DurableOutputDeadLetterStoreConformanceTests
{
    protected override async ValueTask<DurableOutputDeadLetterStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableOutputDeadLetterStoreTestContext.Create(
            store,
            store,
            store,
            database.DisposeAsync);
    }
}

public sealed class TSqlDurableOutputStatusStoreConformanceTests :
    DurableOutputStatusStoreConformanceTests
{
    protected override async ValueTask<DurableOutputStatusStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        await store.TryLeaseAsync(TSqlDurableOutputTestSupport.Request(
            TSqlDurableOutputTestSupport.Now,
            "status-schema-initializer"));
        return DurableOutputStatusStoreTestContext.Create(
            store,
            store,
            store,
            database.DisposeAsync);
    }
}

public sealed class TSqlDurableOutputRetentionStoreConformanceTests :
    DurableOutputRetentionStoreConformanceTests
{
    protected override async ValueTask<DurableOutputRetentionStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableOutputRetentionStoreTestContext.Create(
            store,
            store,
            store,
            store,
            store,
            database.DisposeAsync);
    }
}
