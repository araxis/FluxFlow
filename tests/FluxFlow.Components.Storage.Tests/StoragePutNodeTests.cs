using FluxFlow.Components.Storage;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

// Every test news the node directly over an injected IStorageStore — no engine, no
// registry. Messages travel as FlowMessage<T>; the correlation id flows request ->
// result for free.
public sealed class StoragePutNodeTests
{
    [Fact]
    public async Task Put_StoresRecordAndPreservesCorrelationId()
    {
        var store = new InMemoryStorageStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        await using var node = new StoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" },
            clock);
        var output = StorageTestSink.Link(node.Output);

        var request = FlowMessage.Create(new StoragePutRequest
        {
            Key = "a",
            Value = "one",
            CorrelationId = "c-1"
        });
        await node.Input.SendAsync(request);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(request.CorrelationId);   // the whole point of the envelope
        result.Payload.Succeeded.ShouldBeTrue();
        result.Payload.Operation.ShouldBe("put");
        result.Payload.Collection.ShouldBe("items");
        result.Payload.Key.ShouldBe("a");
        result.Payload.Record!.Value.ShouldBe("one");
        result.Payload.Version.ShouldBe(1);
        result.Payload.Timestamp.ShouldBe(clock.GetUtcNow());
        store.RecordCount.ShouldBe(1);
    }

    [Fact]
    public async Task Put_RequestOverridesNodeMode()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(
            store,
            new StoragePutOptions { Collection = "items", Mode = StorageWriteMode.Create });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        // Seed an existing record (Upsert).
        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest
        {
            Key = "a",
            Value = "one",
            Mode = StorageWriteMode.Upsert
        }));
        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Version.ShouldBe(1);

        // Node mode is Create, but the request overrides it to Replace, which succeeds.
        var replace = FlowMessage.Create(new StoragePutRequest
        {
            Key = "a",
            Value = "two",
            Mode = StorageWriteMode.Replace,
            CorrelationId = "c-2"
        });
        await node.Input.SendAsync(replace);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(replace.CorrelationId);
        result.Payload.Version.ShouldBe(2);
        result.Payload.Record!.Value.ShouldBe("two");
        errors.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Put_SuppressesStoredRecordWhenConfigured()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(
            store,
            new StoragePutOptions { Collection = "items", EmitStoredRecord = false });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "a", Value = "one" }));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Succeeded.ShouldBeTrue();
        result.Payload.Record.ShouldBeNull();
        result.Payload.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Put_ReportsInvalidRequestAndContinues()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        var bad = FlowMessage.Create(new StoragePutRequest { Key = "", Value = "bad", CorrelationId = "bad-1" });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "good", Value = "ok" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        error.CorrelationId.ShouldBe(bad.CorrelationId);

        // The node keeps processing the next (valid) message.
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Key.ShouldBe("good");
        store.RecordCount.ShouldBe(1);
    }

    [Fact]
    public async Task Put_ReportsInvalidRequestWhenCollectionMissing()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(store);   // no default collection
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "a", Value = "one" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        error.Message.ShouldContain("collection");
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("operation=put");
    }

    [Fact]
    public async Task Put_ReportsInvalidRequestForUnsupportedModeAndContinues()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        var bad = FlowMessage.Create(new StoragePutRequest
        {
            Key = "bad",
            Value = "bad",
            Mode = (StorageWriteMode)999,
            CorrelationId = "bad-mode"
        });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest
        {
            Key = "good",
            Value = "ok"
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Message.ShouldContain("write mode");

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Key.ShouldBe("good");
        store.RecordCount.ShouldBe(1);
    }

    [Fact]
    public async Task Put_ReportsStoreFailureAsErrorWithCorrelationId()
    {
        var store = new InMemoryStorageStore
        {
            FailWith = () => new InvalidOperationException("disk on fire")
        };
        await using var node = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        var request = FlowMessage.Create(new StoragePutRequest { Key = "a", Value = "one", CorrelationId = "c-9" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.PutFailed);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        error.Message.ShouldContain("disk on fire");
        error.Exception.ShouldBeOfType<InvalidOperationException>();
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("operation=put");
        error.Context.ShouldContain("collection=items");
        error.Context.ShouldContain("key=a");
        error.Context.ShouldContain("correlationId=c-9");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Put_ReportsNullStoreRecordAsError()
    {
        await using var node = new StoragePutNode(
            new NullPutStore(),
            new StoragePutOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest
        {
            Key = "a",
            Value = "one"
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.PutFailed);
        error.Message.ShouldContain("null record");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Put_EmitsStoredEventThenFailedEvent()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var events = StorageTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "a", Value = "one" }));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        var emitted = await StorageTestSink.DrainUntilCompletedAsync(events);
        var @event = emitted.ShouldHaveSingleItem();
        @event.Name.ShouldBe(StorageDiagnosticNames.PutStored);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.Attributes["operation"].ShouldBe("put");
        @event.Attributes["collection"].ShouldBe("items");
    }

    [Fact]
    public async Task Put_EmitsFailedEventOnStoreFailure()
    {
        var store = new InMemoryStorageStore
        {
            FailWith = () => new InvalidOperationException("nope")
        };
        await using var node = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var errors = StorageTestSink.Link(node.Errors);
        var events = StorageTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "a", Value = "one" }));
        await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        var emitted = await StorageTestSink.DrainUntilCompletedAsync(events);
        var @event = emitted.ShouldHaveSingleItem();
        @event.Name.ShouldBe(StorageDiagnosticNames.PutFailed);
        @event.Level.ShouldBe(FlowEventLevel.Error);
    }

    [Fact]
    public async Task Output_FansOutEveryResultToEveryConsumer()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var logger = StorageTestSink.Link(node.Output);
        var mapper = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "a", Value = "one" }));
        await node.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "b", Value = "two" }));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Key.ShouldBe("a");
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Key.ShouldBe("b");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Key.ShouldBe("a");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Key.ShouldBe("b");
    }

    [Fact]
    public void Put_RejectsNullStore()
        => Should.Throw<ArgumentNullException>(() => new StoragePutNode(null!));

    private sealed class NullPutStore : IStorageStore
    {
        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StorageRecord>(null!);

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
