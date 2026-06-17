using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageStoreLifecycleTests
{
    [Fact]
    public async Task ConnectAsync_OpensStoreOnceAndExposesIt()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store, ownLease: false);
        var (_, _, handle) = CreateHarness(factory);

        handle.State.ShouldBe(StorageStoreConnectionState.Disconnected);
        handle.TryGetStore(out _).ShouldBeFalse();

        await handle.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

        handle.State.ShouldBe(StorageStoreConnectionState.Connected);
        factory.OpenCalls.ShouldBe(1);
        handle.TryGetStore(out var borrowed).ShouldBeTrue();
        borrowed.ShouldBeSameAs(store);

        // Idempotent: a second connect does not open a second store.
        await handle.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        factory.OpenCalls.ShouldBe(1);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task Operations_RoundTripThroughTheOpenedStore()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store);
        var (registry, resources, handle) = CreateHarness(factory);

        await handle.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // PUT
        var put = StorageResourceTestContext.CreateOperationNode(
            registry, resources, StorageComponentTypes.Put,
            new { store = StorageResourceTestContext.StoreName, collection = "items" });
        var putResult = await RunSingleAsync<StoragePutRequest, StorageResult>(
            put, StorageComponentPorts.Result,
            new StoragePutRequest { Key = "a", Value = "one", CorrelationId = "c-1" });
        putResult.Succeeded.ShouldBeTrue();
        putResult.Operation.ShouldBe("put");
        putResult.Key.ShouldBe("a");
        putResult.Record!.Value.ShouldBe("one");
        putResult.Version.ShouldBe(1);
        store.RecordCount.ShouldBe(1);

        // GET (found)
        var get = StorageResourceTestContext.CreateOperationNode(
            registry, resources, StorageComponentTypes.Get,
            new { store = StorageResourceTestContext.StoreName, collection = "items" });
        var found = LinkOutput<StorageResult>(get, StorageComponentPorts.Found);
        var getResult = await RunSingleAsync<StorageGetRequest, StorageResult>(
            get, StorageComponentPorts.Result,
            new StorageGetRequest { Key = "a" });
        getResult.Found.ShouldBeTrue();
        getResult.Record!.Value.ShouldBe("one");
        found.TryReceive(out var foundResult).ShouldBeTrue();
        foundResult!.Record!.Value.ShouldBe("one");

        // QUERY
        var query = StorageResourceTestContext.CreateOperationNode(
            registry, resources, StorageComponentTypes.Query,
            new { store = StorageResourceTestContext.StoreName, collection = "items" });
        var records = LinkOutput<StorageRecord>(query, StorageComponentPorts.Records);
        var queryResult = await RunSingleAsync<StorageQueryRequest, StorageQueryResult>(
            query, StorageComponentPorts.Result,
            new StorageQueryRequest { CorrelationId = "q-1" });
        queryResult.Count.ShouldBe(1);
        queryResult.Records.ShouldHaveSingleItem().Key.ShouldBe("a");
        records.TryReceive(out var queriedRecord).ShouldBeTrue();
        queriedRecord!.Key.ShouldBe("a");

        // DELETE
        var delete = StorageResourceTestContext.CreateOperationNode(
            registry, resources, StorageComponentTypes.Delete,
            new { store = StorageResourceTestContext.StoreName, collection = "items" });
        var deleteResult = await RunSingleAsync<StorageDeleteRequest, StorageResult>(
            delete, StorageComponentPorts.Result,
            new StorageDeleteRequest { Key = "a" });
        deleteResult.Deleted.ShouldBeTrue();
        deleteResult.Found.ShouldBeTrue();
        store.RecordCount.ShouldBe(0);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task Put_ReportsStoreNotAvailableBeforeConnect()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store);
        var (registry, resources, handle) = CreateHarness(factory);

        var put = StorageResourceTestContext.CreateOperationNode(
            registry, resources, StorageComponentTypes.Put,
            new { store = StorageResourceTestContext.StoreName, collection = "items" });
        var error = await RunSingleErrorAsync<StoragePutRequest>(
            put, new StoragePutRequest { Key = "a", Value = "one" });

        error.Code.ShouldBe(StorageErrorCodes.StoreNotAvailable);
        factory.OpenCalls.ShouldBe(0);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_StopsBorrowsAndDisposesStoreOnce()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store, ownLease: true);
        var (registry, resources, handle) = CreateHarness(factory);

        await handle.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        handle.TryGetStore(out _).ShouldBeTrue();

        await handle.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

        handle.State.ShouldBe(StorageStoreConnectionState.Disconnected);
        handle.TryGetStore(out _).ShouldBeFalse();
        store.DisposeCalls.ShouldBe(1);

        // Idempotent disconnect does not double-dispose.
        await handle.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        store.DisposeCalls.ShouldBe(1);

        // After disconnect an operation reports not available again.
        var put = StorageResourceTestContext.CreateOperationNode(
            registry, resources, StorageComponentTypes.Put,
            new { store = StorageResourceTestContext.StoreName, collection = "items" });
        var error = await RunSingleErrorAsync<StoragePutRequest>(
            put, new StoragePutRequest { Key = "a", Value = "one" });
        error.Code.ShouldBe(StorageErrorCodes.StoreNotAvailable);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task SharedStore_IsNotDisposedOnDisconnect()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store, ownLease: false);
        var (_, _, handle) = CreateHarness(factory);

        await handle.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await handle.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // Shared (host-owned) lease => store must not be disposed.
        store.DisposeCalls.ShouldBe(0);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_IsSingleFlight_TwoConcurrentConnectsOpenOnce()
    {
        var gate = new TaskCompletionSource();
        var store = new InMemoryStorageStore();
        var factory = new GatedStorageStoreFactory(store, gate.Task);
        var (_, _, handle) = CreateHarness(factory);

        var first = handle.ConnectAsync();
        var second = handle.ConnectAsync();

        // Both calls observe the same in-flight open; release the factory.
        gate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        factory.OpenCalls.ShouldBe(1);
        handle.State.ShouldBe(StorageStoreConnectionState.Connected);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_WithMissingFactory_ReportsOpenFailedAndDoesNotFaultNode()
    {
        // No store factory => default MissingStorageStoreFactory, whose OpenAsync throws.
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(registry);
        var node = (StorageStoreNode)resources[new NodeName(StorageResourceTestContext.StoreName)].Node;

        var diagnostics = new BufferBlock<FlowDiagnostic>();
        node.Diagnostics.LinkTo(
            diagnostics,
            new DataflowLinkOptions { PropagateCompletion = true });

        var connect = node.ConnectAsync();
        await Should.ThrowAsync<InvalidOperationException>(
            connect.WaitAsync(TimeSpan.FromSeconds(5)));

        node.State.ShouldBe(StorageStoreConnectionState.Faulted);
        node.TryGetStore(out _).ShouldBeFalse();

        // The resource node itself is never faulted / the runtime is never torn down.
        node.Completion.IsCompleted.ShouldBeFalse();

        diagnostics.TryReceive(out var diagnostic).ShouldBeTrue();
        diagnostic!.Name.ShouldBe(StorageDiagnosticNames.StoreOpenFailed);
        diagnostic.Level.ShouldBe(FlowDiagnosticLevel.Error);

        await ((IAsyncDisposable)node).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_TearsDownStoreOnceIdempotently()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store, ownLease: true);
        var (_, _, handle) = CreateHarness(factory);

        await handle.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await ((IAsyncDisposable)handle).DisposeAsync();

        store.DisposeCalls.ShouldBe(1);

        // Idempotent dispose.
        await ((IAsyncDisposable)handle).DisposeAsync();
        store.DisposeCalls.ShouldBe(1);
    }

    private static (
        RuntimeNodeFactoryRegistry Registry,
        IReadOnlyDictionary<NodeName, RuntimeNode> Resources,
        IStorageStoreHandle Handle) CreateHarness(IStorageStoreFactory factory)
    {
        var registry = StorageResourceTestContext.CreateRegistry(storeFactory: factory);
        var resources = StorageResourceTestContext.CreateResources(registry);
        var handle = StorageResourceTestContext.ResolveHandle(resources);
        return (registry, resources, handle);
    }

    private static async Task<TOutput> RunSingleAsync<TInput, TOutput>(
        RuntimeNode runtimeNode,
        string outputPort,
        TInput request)
    {
        var input = runtimeNode.FindInput(new PortName(StorageComponentPorts.Input))
            .ShouldBeOfType<InputPort<TInput>>();
        var output = LinkOutput<TOutput>(runtimeNode, outputPort);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(request);
        input.Target.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        return result;
    }

    private static async Task<FlowError> RunSingleErrorAsync<TInput>(
        RuntimeNode runtimeNode,
        TInput request)
    {
        var input = runtimeNode.FindInput(new PortName(StorageComponentPorts.Input))
            .ShouldBeOfType<InputPort<TInput>>();
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(request);
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        return error;
    }

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("sink"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }
}
