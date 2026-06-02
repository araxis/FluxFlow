using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageComponentTests
{
    [Fact]
    public async Task Put_UpsertsRecordAndEmitsResult()
    {
        var store = new TestStorageStore();
        var runtimeNode = CreatePut(new
        {
            collection = "items",
            boundedCapacity = 4
        }, store);
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest
        {
            Key = "a",
            Value = "one",
            CorrelationId = "c-1"
        });
        input.Target.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Operation.ShouldBe("put");
        result.Collection.ShouldBe("items");
        result.Key.ShouldBe("a");
        result.Succeeded.ShouldBeTrue();
        result.Found.ShouldBeTrue();
        result.Version.ShouldBe(1);
        result.Record.ShouldNotBeNull();
        result.Record.Value.ShouldBe("one");
        result.CorrelationId.ShouldBe("c-1");
        store.Records.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Put_ReportsFailureAndContinues()
    {
        var store = new TestStorageStore { FailNextPut = true };
        var runtimeNode = CreatePut(new
        {
            collection = "items"
        }, store);
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest { Key = "bad", Value = "bad" });
        await input.Target.SendAsync(new StoragePutRequest { Key = "good", Value = "ok" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StorageErrorCodes.PutFailed);
        result.Key.ShouldBe("good");
        store.Records.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Put_CreateModeRejectsExistingRecordAndContinues()
    {
        var store = new TestStorageStore();
        var runtimeNode = CreatePut(new
        {
            collection = "items",
            mode = "create"
        }, store);
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest { Key = "a", Value = "one" });
        await input.Target.SendAsync(new StoragePutRequest { Key = "a", Value = "duplicate" });
        await input.Target.SendAsync(new StoragePutRequest { Key = "b", Value = "two" });
        input.Target.Complete();

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Key.ShouldBe("a");
        error.Code.ShouldBe(StorageErrorCodes.PutFailed);
        second.Key.ShouldBe("b");
        store.Records.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Get_RoutesFoundAndNotFound()
    {
        var store = new TestStorageStore();
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "a",
            Value = "one",
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow
        });
        var runtimeNode = CreateGet(new
        {
            collection = "items"
        }, store);
        var input = GetInput<StorageGetRequest>(runtimeNode);
        var resultOutput = LinkOutput<StorageResult>(runtimeNode);
        var foundOutput = LinkOutput<StorageResult>(runtimeNode, StorageComponentPorts.Found);
        var notFoundOutput = LinkOutput<StorageResult>(runtimeNode, StorageComponentPorts.NotFound);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageGetRequest { Key = "a" });
        await input.Target.SendAsync(new StorageGetRequest { Key = "missing" });
        input.Target.Complete();

        var results = await DrainUntilCompletedAsync(resultOutput);
        var found = await foundOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var notFound = await notFoundOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        results.Count.ShouldBe(2);
        found.Found.ShouldBeTrue();
        found.Record.ShouldNotBeNull();
        notFound.Found.ShouldBeFalse();
        notFound.Key.ShouldBe("missing");
    }

    [Fact]
    public async Task Get_CanIncludeExpiredRecords()
    {
        var store = new TestStorageStore();
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "expired",
            Value = "old",
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var runtimeNode = CreateGet(new
        {
            collection = "items"
        }, store);
        var input = GetInput<StorageGetRequest>(runtimeNode);
        var resultOutput = LinkOutput<StorageResult>(runtimeNode);
        var foundOutput = LinkOutput<StorageResult>(runtimeNode, StorageComponentPorts.Found);
        var notFoundOutput = LinkOutput<StorageResult>(runtimeNode, StorageComponentPorts.NotFound);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageGetRequest { Key = "expired" });
        await input.Target.SendAsync(new StorageGetRequest { Key = "expired", IncludeExpired = true });
        input.Target.Complete();

        var missing = await notFoundOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var found = await foundOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var results = await DrainUntilCompletedAsync(resultOutput);
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        missing.Found.ShouldBeFalse();
        found.Found.ShouldBeTrue();
        found.Record!.Value.ShouldBe("old");
        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Query_EmitsSummaryAndRecordOutputs()
    {
        var store = new TestStorageStore();
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "a-1",
            Value = "one",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" },
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow.AddMinutes(-3)
        });
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "a-2",
            Value = "two",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" },
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "b-1",
            Value = "three",
            Attributes = new Dictionary<string, string> { ["kind"] = "beta" },
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var runtimeNode = CreateQuery(new
        {
            collection = "items",
            limit = 10
        }, store);
        var input = GetInput<StorageQueryRequest>(runtimeNode);
        var resultOutput = LinkOutput<StorageQueryResult>(runtimeNode);
        var recordsOutput = LinkOutput<StorageRecord>(runtimeNode, StorageComponentPorts.Records);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageQueryRequest
        {
            KeyPrefix = "a-",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" },
            CorrelationId = "query-a"
        });
        input.Target.Complete();

        var result = await resultOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var records = await DrainUntilCompletedAsync(recordsOutput);
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Operation.ShouldBe("query");
        result.Collection.ShouldBe("items");
        result.Succeeded.ShouldBeTrue();
        result.Count.ShouldBe(2);
        result.CorrelationId.ShouldBe("query-a");
        result.Records.Select(record => record.Key).ShouldBe(["a-1", "a-2"]);
        records.Select(record => record.Key).ShouldBe(["a-1", "a-2"]);
    }

    [Fact]
    public async Task Query_CanSuppressRecordPayloadsAndOutputs()
    {
        var store = new TestStorageStore();
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "a",
            Value = "one",
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow
        });
        var runtimeNode = CreateQuery(new
        {
            collection = "items",
            emitRecordsInResult = false,
            emitRecordOutputs = false
        }, store);
        var input = GetInput<StorageQueryRequest>(runtimeNode);
        var resultOutput = LinkOutput<StorageQueryResult>(runtimeNode);
        var recordsOutput = LinkOutput<StorageRecord>(runtimeNode, StorageComponentPorts.Records);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageQueryRequest());
        input.Target.Complete();

        var result = await resultOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var records = await DrainUntilCompletedAsync(recordsOutput);
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Count.ShouldBe(1);
        result.Records.ShouldBeEmpty();
        records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_ReportsFailureAndContinues()
    {
        var store = new TestStorageStore { FailNextQuery = true };
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "a",
            Value = "one",
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow
        });
        var runtimeNode = CreateQuery(new
        {
            collection = "items"
        }, store);
        var input = GetInput<StorageQueryRequest>(runtimeNode);
        var output = LinkOutput<StorageQueryResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageQueryRequest());
        await input.Target.SendAsync(new StorageQueryRequest());
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StorageErrorCodes.QueryFailed);
        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Delete_EmitsDeletedAndMissingResults()
    {
        var store = new TestStorageStore();
        store.Seed(new StorageRecord
        {
            Collection = "items",
            Key = "a",
            Value = "one",
            Version = 1,
            StoredAt = DateTimeOffset.UtcNow
        });
        var runtimeNode = CreateDelete(new
        {
            collection = "items"
        }, store);
        var input = GetInput<StorageDeleteRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageDeleteRequest { Key = "a" });
        await input.Target.SendAsync(new StorageDeleteRequest { Key = "missing" });
        input.Target.Complete();

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Found.ShouldBeTrue();
        first.Deleted.ShouldBeTrue();
        second.Found.ShouldBeFalse();
        second.Deleted.ShouldBeFalse();
        store.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_CanSuppressMissingResults()
    {
        var store = new TestStorageStore();
        var runtimeNode = CreateDelete(new
        {
            collection = "items",
            emitMissingAsResult = false
        }, store);
        var input = GetInput<StorageDeleteRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageDeleteRequest { Key = "missing" });
        input.Target.Complete();

        var results = await DrainUntilCompletedAsync(output);
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nodes_FailStartupWhenFactoryFails()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseStore(
                (_, _) => throw new InvalidOperationException("open failed")));
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();
        var runtimeNode = factory(CreateContext(StorageComponentTypes.Put, new
        {
            collection = "items"
        }));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());

        exception.Message.ShouldContain("open failed");
    }

    [Fact]
    public void Put_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreatePut(new { boundedCapacity = 0 }, new TestStorageStore()));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public async Task Put_ReportsInvalidRequestAndContinues()
    {
        var store = new TestStorageStore();
        var runtimeNode = CreatePut(new
        {
            collection = "items"
        }, store);
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StorageComponentPorts.Errors);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest { Key = "", Value = "bad" });
        await input.Target.SendAsync(new StoragePutRequest { Key = "good", Value = "ok" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        result.Key.ShouldBe("good");
    }

    [Fact]
    public async Task Put_EmitsDiagnostics()
    {
        var store = new TestStorageStore();
        var runtimeNode = CreatePut(new
        {
            collection = "items"
        }, store);
        var input = GetInput<StoragePutRequest>(runtimeNode);
        var output = LinkOutput<StorageResult>(runtimeNode);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest { Key = "a", Value = "one" });
        input.Target.Complete();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var names = (await DrainDiagnosticsUntilCompletedAsync(diagnostics))
            .Select(diagnostic => diagnostic.Name)
            .ToArray();
        names.ShouldContain(StorageDiagnosticNames.StoreOpened);
        names.ShouldContain(StorageDiagnosticNames.PutStored);
    }

    [Fact]
    public async Task StorageStoreLease_DisposesOwnedStoreOnly()
    {
        var ownedStore = new DisposableStorageStore();
        var sharedStore = new DisposableStorageStore();

        await StorageStoreLease.Owned(ownedStore).DisposeAsync();
        await StorageStoreLease.Shared(sharedStore).DisposeAsync();

        ownedStore.Disposed.ShouldBeTrue();
        sharedStore.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task NodeDispose_ReleasesOwnedStoreAfterFault()
    {
        var store = new DisposableStorageStore();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseStore(
                (_, _) => ValueTask.FromResult(StorageStoreLease.Owned(store))));
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();
        var runtimeNode = factory(CreateContext(StorageComponentTypes.Put, new
        {
            collection = "items"
        }));

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Fault(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await ((IAsyncDisposable)runtimeNode.Node).DisposeAsync());

        store.Disposed.ShouldBeTrue();
    }

    [Fact]
    public void RegisterStorageComponents_RegistersNodes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseSharedStore(new TestStorageStore()));

        registry.TryGetFactory(StorageComponentTypes.Put, out _).ShouldBeTrue();
        registry.TryGetFactory(StorageComponentTypes.Get, out _).ShouldBeTrue();
        registry.TryGetFactory(StorageComponentTypes.Query, out _).ShouldBeTrue();
        registry.TryGetFactory(StorageComponentTypes.Delete, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreatePut(
        object configuration,
        TestStorageStore store)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseSharedStore(store));
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();
        return factory(CreateContext(StorageComponentTypes.Put, configuration));
    }

    private static RuntimeNode CreateGet(
        object configuration,
        TestStorageStore store)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseSharedStore(store));
        registry.TryGetFactory(StorageComponentTypes.Get, out var factory).ShouldBeTrue();
        return factory(CreateContext(StorageComponentTypes.Get, configuration));
    }

    private static RuntimeNode CreateDelete(
        object configuration,
        TestStorageStore store)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseSharedStore(store));
        registry.TryGetFactory(StorageComponentTypes.Delete, out var factory).ShouldBeTrue();
        return factory(CreateContext(StorageComponentTypes.Delete, configuration));
    }

    private static RuntimeNode CreateQuery(
        object configuration,
        TestStorageStore store)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseSharedStore(store));
        registry.TryGetFactory(StorageComponentTypes.Query, out var factory).ShouldBeTrue();
        return factory(CreateContext(StorageComponentTypes.Query, configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(
        NodeType nodeType,
        object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("storage"),
            new NodeDefinition
            {
                Type = nodeType,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
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

    private static async Task<IReadOnlyList<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> output)
    {
        var values = new List<T>();
        while (await output.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            while (output.TryReceive(out var value))
            {
                values.Add(value);
            }
        }

        return values;
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

    private class TestStorageStore : IStorageStore
    {
        private readonly Dictionary<(string Collection, string Key), StorageRecord> _records = [];

        public IReadOnlyCollection<StorageRecord> Records => _records.Values.ToArray();

        public bool FailNextPut { get; set; }
        public bool FailNextGet { get; set; }
        public bool FailNextQuery { get; set; }
        public bool FailNextDelete { get; set; }

        public void Seed(StorageRecord record)
            => _records[(record.Collection, record.Key)] = CopyRecord(record);

        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailNextPut)
            {
                FailNextPut = false;
                throw new InvalidOperationException("put failed");
            }

            var key = (request.Collection!, request.Key);
            _records.TryGetValue(key, out var existing);
            var mode = request.Mode ?? StorageWriteMode.Upsert;
            if (mode == StorageWriteMode.Create && existing is not null)
            {
                throw new InvalidOperationException("record already exists");
            }

            if (mode == StorageWriteMode.Replace && existing is null)
            {
                throw new InvalidOperationException("record does not exist");
            }

            if (request.ExpectedVersion.HasValue &&
                (existing?.Version ?? 0) != request.ExpectedVersion.Value)
            {
                throw new InvalidOperationException("record version did not match");
            }

            var record = new StorageRecord
            {
                Collection = request.Collection!,
                Key = request.Key,
                Value = request.Value,
                ContentType = request.ContentType,
                Attributes = CopyAttributes(request.Attributes),
                Version = (existing?.Version ?? 0) + 1,
                StoredAt = DateTimeOffset.UtcNow,
                ExpiresAt = request.ExpiresAt,
                CorrelationId = request.CorrelationId
            };
            _records[key] = record;
            return Task.FromResult(CopyRecord(record));
        }

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailNextGet)
            {
                FailNextGet = false;
                throw new InvalidOperationException("get failed");
            }

            if (!_records.TryGetValue((request.Collection!, request.Key), out var record))
            {
                return Task.FromResult<StorageRecord?>(null);
            }

            if (record.ExpiresAt.HasValue &&
                record.ExpiresAt.Value <= DateTimeOffset.UtcNow &&
                request.IncludeExpired != true)
            {
                return Task.FromResult<StorageRecord?>(null);
            }

            return Task.FromResult<StorageRecord?>(CopyRecord(record));
        }

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailNextQuery)
            {
                FailNextQuery = false;
                throw new InvalidOperationException("query failed");
            }

            var records = _records.Values
                .Where(record => StringComparer.Ordinal.Equals(record.Collection, request.Collection))
                .Where(record => string.IsNullOrWhiteSpace(request.KeyPrefix) ||
                    record.Key.StartsWith(request.KeyPrefix, StringComparison.Ordinal))
                .Where(record => !request.StoredFrom.HasValue || record.StoredAt >= request.StoredFrom.Value)
                .Where(record => !request.StoredTo.HasValue || record.StoredAt <= request.StoredTo.Value)
                .Where(record => !record.ExpiresAt.HasValue ||
                    record.ExpiresAt.Value > DateTimeOffset.UtcNow ||
                    request.IncludeExpired == true)
                .Where(record => MatchesAttributes(record, request.Attributes))
                .OrderBy(record => record.StoredAt)
                .ThenBy(record => record.Key, StringComparer.Ordinal)
                .Take(request.Limit ?? int.MaxValue)
                .Select(CopyRecord)
                .ToArray();

            return Task.FromResult<IReadOnlyList<StorageRecord>>(records);
        }

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailNextDelete)
            {
                FailNextDelete = false;
                throw new InvalidOperationException("delete failed");
            }

            var key = (request.Collection!, request.Key);
            var found = _records.Remove(key, out var record);
            return Task.FromResult(new StorageResult
            {
                Timestamp = DateTimeOffset.UtcNow,
                Operation = "delete",
                Collection = request.Collection!,
                Key = request.Key,
                Succeeded = true,
                Found = found,
                Deleted = found,
                Record = record is null ? null : CopyRecord(record),
                Version = record?.Version,
                CorrelationId = request.CorrelationId,
                Attributes = record is null ? [] : CopyAttributes(record.Attributes)
            });
        }

        protected static StorageRecord CopyRecord(StorageRecord record)
            => record with
            {
                Attributes = CopyAttributes(record.Attributes)
            };

        protected static Dictionary<string, string> CopyAttributes(
            Dictionary<string, string>? attributes)
            => attributes is null
                ? []
                : new Dictionary<string, string>(attributes, StringComparer.Ordinal);

        private static bool MatchesAttributes(
            StorageRecord record,
            Dictionary<string, string> attributes)
        {
            foreach (var (name, value) in attributes)
            {
                if (!record.Attributes.TryGetValue(name, out var current) ||
                    !StringComparer.Ordinal.Equals(current, value))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class DisposableStorageStore : TestStorageStore, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
