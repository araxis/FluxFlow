using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageComponentTests
{
    [Fact]
    public void StoreNode_ExposesConfiguredName()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(
            registry,
            new { storeName = "documents" },
            storeName: "documents");

        var handle = resources[new NodeName("documents")].Node
            .ShouldBeAssignableTo<IStorageStoreHandle>()!;

        handle.StoreName.ShouldBe("documents");
    }

    [Fact]
    public void StoreNode_DefaultsNameToResourceNodeName()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(registry);

        var handle = resources[new NodeName(StorageResourceTestContext.StoreName)].Node
            .ShouldBeAssignableTo<IStorageStoreHandle>()!;

        handle.StoreName.ShouldBe(StorageResourceTestContext.StoreName);
    }

    [Fact]
    public void StoreNode_RejectsEmptyStoreName()
    {
        var registry = StorageResourceTestContext.CreateRegistry();

        var exception = Should.Throw<InvalidOperationException>(
            () => StorageResourceTestContext.CreateResources(
                registry,
                new { storeName = "" }));

        exception.Message.ShouldContain("storeName");
    }

    [Fact]
    public async Task Put_ReportsStoreNotAvailableForValidRequest()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        var runtimeNode = CreatePut(new
        {
            store = StorageResourceTestContext.StoreName,
            collection = "items",
            boundedCapacity = 4
        }, clock);
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest
        {
            Key = "a",
            Value = "one",
            CorrelationId = "c-1"
        });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // No store => no result is produced; the node reports not available.
        output.TryReceive(out _).ShouldBeFalse();
        error.Code.ShouldBe(StorageErrorCodes.StoreNotAvailable);
        error.Message.ShouldContain("host ConnectAsync");
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("operation=put");
        error.Context.ShouldContain("collection=items");
        error.Context.ShouldContain("key=a");
        error.Context.ShouldContain("correlationId=c-1");
        error.Context.ShouldContain($"store={StorageResourceTestContext.StoreName}");
    }

    [Fact]
    public async Task Put_EmitsNotAvailableDiagnostic()
    {
        var runtimeNode = CreatePut(new
        {
            store = StorageResourceTestContext.StoreName,
            collection = "items"
        });
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest { Key = "a", Value = "one" });
        input.Target.Complete();
        await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var emitted = await DrainDiagnosticsUntilCompletedAsync(diagnostics);
        var diagnostic = emitted.ShouldHaveSingleItem();
        diagnostic.Name.ShouldBe(StorageDiagnosticNames.PutFailed);
        diagnostic.Level.ShouldBe(FlowDiagnosticLevel.Error);
        diagnostic.Attributes["store"].ShouldBe(StorageResourceTestContext.StoreName);
    }

    [Fact]
    public async Task Get_ReportsStoreNotAvailableForValidRequest()
    {
        var runtimeNode = CreateGet(new
        {
            store = StorageResourceTestContext.StoreName,
            collection = "items"
        });
        var input = GetInput<StorageGetRequest>(runtimeNode);
        var resultOutput = LinkOutput<StorageResult>(runtimeNode);
        var foundOutput = LinkOutput<StorageResult>(runtimeNode, StorageComponentPorts.Found);
        var notFoundOutput = LinkOutput<StorageResult>(runtimeNode, StorageComponentPorts.NotFound);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageGetRequest { Key = "a" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StorageErrorCodes.StoreNotAvailable);
        error.Message.ShouldContain("storage.get");
        resultOutput.TryReceive(out _).ShouldBeFalse();
        foundOutput.TryReceive(out _).ShouldBeFalse();
        notFoundOutput.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Query_ReportsStoreNotAvailableForValidRequest()
    {
        var runtimeNode = CreateQuery(new
        {
            store = StorageResourceTestContext.StoreName,
            collection = "items",
            limit = 10
        });
        var input = GetInput<StorageQueryRequest>(runtimeNode);
        var resultOutput = LinkOutput<StorageQueryResult>(runtimeNode);
        var recordsOutput = LinkOutput<StorageRecord>(runtimeNode, StorageComponentPorts.Records);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageQueryRequest { CorrelationId = "query-a" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StorageErrorCodes.StoreNotAvailable);
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("operation=query");
        error.Context.ShouldContain("collection=items");
        error.Context.ShouldContain("correlationId=query-a");
        resultOutput.TryReceive(out _).ShouldBeFalse();
        recordsOutput.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_ReportsStoreNotAvailableForValidRequest()
    {
        var runtimeNode = CreateDelete(new
        {
            store = StorageResourceTestContext.StoreName,
            collection = "items"
        });
        var input = GetInput<StorageDeleteRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageDeleteRequest { Key = "a" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StorageErrorCodes.StoreNotAvailable);
        error.Message.ShouldContain("storage.delete");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Put_ReportsInvalidRequestAndContinues()
    {
        var runtimeNode = CreatePut(new
        {
            store = StorageResourceTestContext.StoreName,
            collection = "items"
        });
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest { Key = "", Value = "bad" });
        await input.Target.SendAsync(new StoragePutRequest { Key = "good", Value = "ok" });
        input.Target.Complete();

        var first = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        second.Code.ShouldBe(StorageErrorCodes.StoreNotAvailable);
    }

    [Fact]
    public async Task Get_ReportsInvalidRequestWhenCollectionMissing()
    {
        var runtimeNode = CreateGet(new
        {
            store = StorageResourceTestContext.StoreName
        });
        var input = GetInput<StorageGetRequest>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageGetRequest { Key = "a" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        error.Message.ShouldContain("collection");
    }

    [Fact]
    public void Put_FailsWhenStoreResourceMissing()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(StorageResourceTestContext.CreateContext(
                StorageComponentTypes.Put,
                new { store = "missing-store", collection = "items" },
                new Dictionary<NodeName, RuntimeNode>())));

        exception.Message.ShouldContain("missing-store");
    }

    [Fact]
    public void Put_RejectsMissingStoreReference()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(StorageResourceTestContext.CreateContext(
                StorageComponentTypes.Put,
                new { collection = "items" },
                resources)));

        exception.Message.ShouldContain("store");
    }

    [Fact]
    public void Put_RejectsEmptyStoreReference()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(StorageResourceTestContext.CreateContext(
                StorageComponentTypes.Put,
                new { store = "", collection = "items" },
                resources)));

        exception.Message.ShouldContain("store");
    }

    [Fact]
    public void Put_RejectsInvalidBoundedCapacity()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(StorageResourceTestContext.CreateContext(
                StorageComponentTypes.Put,
                new { store = StorageResourceTestContext.StoreName, boundedCapacity = 0 },
                resources)));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Query_RejectsInvalidLimit()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(StorageComponentTypes.Query, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(StorageResourceTestContext.CreateContext(
                StorageComponentTypes.Query,
                new { store = StorageResourceTestContext.StoreName, limit = 0 },
                resources)));

        exception.Message.ShouldContain("limit");
    }

    [Fact]
    public void Query_RejectsNegativeOffset()
    {
        var registry = StorageResourceTestContext.CreateRegistry();
        var resources = StorageResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(StorageComponentTypes.Query, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(StorageResourceTestContext.CreateContext(
                StorageComponentTypes.Query,
                new { store = StorageResourceTestContext.StoreName, offset = -1 },
                resources)));

        exception.Message.ShouldContain("offset");
    }

    [Fact]
    public void RegisterStorageComponents_RegistersNodes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents();

        registry.TryGetFactory(StorageComponentTypes.Store, out _).ShouldBeTrue();
        registry.TryGetFactory(StorageComponentTypes.Put, out _).ShouldBeTrue();
        registry.TryGetFactory(StorageComponentTypes.Get, out _).ShouldBeTrue();
        registry.TryGetFactory(StorageComponentTypes.Query, out _).ShouldBeTrue();
        registry.TryGetFactory(StorageComponentTypes.Delete, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreatePut(object configuration, FakeTimeProvider? clock = null)
        => CreateOperation(StorageComponentTypes.Put, configuration, clock);

    private static RuntimeNode CreateGet(object configuration, FakeTimeProvider? clock = null)
        => CreateOperation(StorageComponentTypes.Get, configuration, clock);

    private static RuntimeNode CreateDelete(object configuration, FakeTimeProvider? clock = null)
        => CreateOperation(StorageComponentTypes.Delete, configuration, clock);

    private static RuntimeNode CreateQuery(object configuration, FakeTimeProvider? clock = null)
        => CreateOperation(StorageComponentTypes.Query, configuration, clock);

    private static RuntimeNode CreateOperation(
        NodeType nodeType,
        object configuration,
        FakeTimeProvider? clock)
    {
        var registry = StorageResourceTestContext.CreateRegistry(clock);
        var resources = StorageResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(nodeType, out var factory).ShouldBeTrue();
        return factory(StorageResourceTestContext.CreateContext(nodeType, configuration, resources));
    }

    private static InputPort<TInput> GetInput<TInput>(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(StorageComponentPorts.Input))
            .ShouldBeOfType<InputPort<TInput>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName = StorageComponentPorts.Result)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }

    private static async Task<IReadOnlyList<FlowDiagnostic>> DrainDiagnosticsUntilCompletedAsync(
        BufferBlock<FlowDiagnostic> output)
    {
        var diagnostics = new List<FlowDiagnostic>();
        while (await output.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            while (output.TryReceive(out var diagnostic))
            {
                diagnostics.Add(diagnostic);
            }
        }

        return diagnostics;
    }
}
