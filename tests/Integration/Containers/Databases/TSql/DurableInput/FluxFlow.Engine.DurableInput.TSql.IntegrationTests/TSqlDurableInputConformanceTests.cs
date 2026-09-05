using FluxFlow.Engine.DurableInput.Tests;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlDurableInputStoreConformanceTests : DurableInputStoreConformanceTests
{
    protected override async ValueTask<DurableInputStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableInputStoreTestContext.Create(store, database.DisposeAsync);
    }
}

public sealed class TSqlDurableInputDeadLetterStoreConformanceTests :
    DurableInputDeadLetterStoreConformanceTests
{
    protected override async ValueTask<DurableInputDeadLetterStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableInputDeadLetterStoreTestContext.Create(
            store,
            store,
            database.DisposeAsync);
    }
}

public sealed class TSqlDurableInputLeaseRenewalStoreConformanceTests :
    DurableInputLeaseRenewalStoreConformanceTests
{
    protected override async ValueTask<DurableInputLeaseRenewalStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableInputLeaseRenewalStoreTestContext.Create(
            store,
            store,
            database.DisposeAsync);
    }
}

public sealed class TSqlDurableInputStatusStoreConformanceTests :
    DurableInputStatusStoreConformanceTests
{
    protected override async ValueTask<DurableInputStatusStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        await store.LeaseAsync(new(
            "status-schema-initializer",
            TSqlDurableInputTestSupport.Now,
            TSqlDurableInputTestSupport.Now.AddMinutes(1),
            maxCount: 1));
        return DurableInputStatusStoreTestContext.Create(
            store,
            store,
            database.DisposeAsync);
    }
}

public sealed class TSqlDurableInputRetentionStoreConformanceTests :
    DurableInputRetentionStoreConformanceTests
{
    protected override async ValueTask<DurableInputRetentionStoreTestContext> CreateStoreAsync()
    {
        var database = await TSqlTestDatabase.CreateAsync();
        var store = database.CreateStore();
        return DurableInputRetentionStoreTestContext.Create(
            store,
            store,
            store,
            store,
            database.DisposeAsync);
    }
}
