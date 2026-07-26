using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Storage.Composition.Tests;

public sealed class StorageCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", StorageCompositionPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", StorageCompositionPortNames.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort("main", "node", CompositionComponentEvents.PortName);

    [Fact]
    public void Register_storage_nodes_registers_canonical_metadata()
    {
        var registry = RegisterAll(new CompositionNodeRegistry());

        var put = registry.Registrations[StorageCompositionNodeTypes.Put];
        put.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageContentPutRequest));
        put.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StoragePutOutcome));

        var get = registry.Registrations[StorageCompositionNodeTypes.Get];
        get.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageGetRequest));
        get.Outputs.Keys.ShouldBe([
            StorageCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
        get.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StorageGetOutcome));

        var query = registry.Registrations[StorageCompositionNodeTypes.Query];
        query.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageQueryRequest));
        query.Outputs.Keys.ShouldBe([
            StorageCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
        query.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StorageQueryOutcome));

        var delete = registry.Registrations[StorageCompositionNodeTypes.Delete];
        delete.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StorageDeleteOutcome));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_storage_metadata()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            StorageCompositionNodeTypes.Put,
            StorageCompositionNodeTypes.Get,
            StorageCompositionNodeTypes.Query,
            StorageCompositionNodeTypes.Delete
        ], ignoreOrder: false);
        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("Storage"));
            item.SuggestedEditorWidth.ShouldBe(460);
            AssertResources(item);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_ports()
    {
        var metadata = DesignMetadataByType();

        AssertTransformPorts(
            metadata[StorageCompositionNodeTypes.Put],
            nameof(StorageContentPutRequest),
            "StoragePutOutcome");
        AssertTransformPorts(
            metadata[StorageCompositionNodeTypes.Get],
            nameof(StorageGetRequest),
            "StorageGetOutcome");
        AssertTransformPorts(
            metadata[StorageCompositionNodeTypes.Query],
            nameof(StorageQueryRequest),
            "StorageQueryOutcome");
        AssertTransformPorts(
            metadata[StorageCompositionNodeTypes.Delete],
            nameof(StorageDeleteRequest),
            "StorageDeleteOutcome");
    }

    [Fact]
    public void Design_metadata_provider_exposes_only_canonical_options()
    {
        var metadata = DesignMetadataByType();
        var query = metadata[StorageCompositionNodeTypes.Query];
        var delete = metadata[StorageCompositionNodeTypes.Delete];

        query.Options.Select(option => option.Name.Value).ShouldBe([
            "collection",
            "includeExpired",
            "offset",
            "limit",
            "emitRecordsInResult",
            "boundedCapacity"
        ], ignoreOrder: false);
        query.Attributes.ContainsKey(new ComponentAttributeName("omittedOptions"))
            .ShouldBeFalse();
        delete.Options.Select(option => option.Name.Value).ShouldBe([
            "collection",
            "boundedCapacity"
        ], ignoreOrder: false);
        delete.Attributes.ContainsKey(new ComponentAttributeName("omittedOptions"))
            .ShouldBeFalse();
    }

    [Fact]
    public void Design_metadata_provider_preserves_option_and_resource_hints()
    {
        var metadata = DesignMetadataByType();
        foreach (var item in metadata.Values)
        {
            var options = item.Options.ToDictionary(option => option.Name.Value);
            AssertOptionHints(
                options["collection"],
                "Collection",
                OptionDesignMetadataAttributeValues.Primary,
                OptionDesignMetadataAttributeValues.Text);
            AssertOptionHints(
                options["boundedCapacity"],
                "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number);

            var resources = ResourcesByName(item);
            AssertResourceHints(
                resources[StorageCompositionResourceNames.Store],
                ResourceDesignMetadataAttributeValues.Store,
                "storage-store:{name}");
            AssertResourceHints(
                resources[StorageCompositionResourceNames.Clock],
                ResourceDesignMetadataAttributeValues.Clock,
                "clock:{name}");
        }
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
            [new StorageComponentDesignMetadataProvider()]);

        catalog.All.Count.ShouldBe(4);
        catalog.TryGet(
            new ComponentType(StorageCompositionNodeTypes.Put),
            out var putMetadata).ShouldBeTrue();
        putMetadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("Storage Put");
    }

    [Fact]
    public async Task Hosted_put_resolves_store_binds_options_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-20T15:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new InMemoryStorageStore();

        await WithNodeAsync(
            StorageCompositionNodeTypes.Put,
            async (ports, host) =>
            {
                var message = FlowMessage.Create(
                    new StorageContentPutRequest
                    {
                        Key = "a",
                        Content = FlowContent.FromBytes(
                            new byte[] { 0x00, 0xFF },
                            "application/octet-stream")
                    },
                    new CorrelationId("put-1"));
                var resultReceive = ports.ReceiveAsync<StoragePutOutcome>(
                    Output,
                    Timeout);
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(
                    Events,
                    Timeout);

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.CausationId.ShouldBe(message.MessageId);
                result.IsError.ShouldBeFalse();
                result.Value.Collection.ShouldBe("items");
                result.Value.Record.ShouldNotBeNull()
                    .Content.Bytes.AsSpan().ToArray().ShouldBe([0x00, 0xFF]);
                var @event = (await eventReceive).Message.ShouldNotBeNull().Value;
                @event.Name.ShouldBe(StorageDiagnosticNames.PutStored);
                @event.Timestamp.ShouldBe(timestamp);
                await host.RevisionHost.StopApplicationAsync();
            },
            Properties(
                ("collection", "items"),
                ("mode", StorageWriteMode.Create),
                ("boundedCapacity", 8)),
            store,
            clock);
    }

    [Fact]
    public async Task Hosted_put_resolves_factory_and_disposes_owned_lease()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store);

        await WithNodeAsync(
            StorageCompositionNodeTypes.Put,
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<StoragePutOutcome>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new StorageContentPutRequest
                {
                    Key = "a",
                    Content = FlowContent.FromBytes(new byte[] { 1 })
                }))).IsAccepted.ShouldBeTrue();
                (await resultReceive).Message.ShouldNotBeNull().IsError.ShouldBeFalse();
            },
            Properties(("collection", "items")),
            factory);

        factory.OpenCount.ShouldBe(1);
        factory.Context.ShouldNotBeNull().Collection.ShouldBe("items");
        store.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_get_returns_found_and_missing_on_one_output()
    {
        var store = new InMemoryStorageStore();
        await SeedContentAsync(store, "items", "a", new byte[] { 1, 2 });

        await WithNodeAsync(
            StorageCompositionNodeTypes.Get,
            async (ports, _) =>
            {
                var foundReceive = ports.ReceiveAsync<StorageGetOutcome>(
                    Output,
                    Timeout);
                (await ports.SendAsync(
                    Input,
                    FlowMessage.Create(new StorageGetRequest { Key = "a" })))
                    .IsAccepted.ShouldBeTrue();

                var found = (await foundReceive).Message.ShouldNotBeNull();
                found.IsError.ShouldBeFalse();
                found.Value.Found.ShouldBeTrue();
                found.Value.Record.ShouldNotBeNull()
                    .Content.Bytes.AsSpan().ToArray().ShouldBe([1, 2]);

                var missingReceive = ports.ReceiveAsync<StorageGetOutcome>(
                    Output,
                    Timeout);
                (await ports.SendAsync(
                    Input,
                    FlowMessage.Create(new StorageGetRequest { Key = "missing" })))
                    .IsAccepted.ShouldBeTrue();
                var missing = (await missingReceive).Message.ShouldNotBeNull();
                missing.IsError.ShouldBeFalse();
                missing.Value.Found.ShouldBeFalse();
            },
            Properties(("collection", "items")),
            store);
    }

    [Fact]
    public async Task Hosted_query_returns_one_bounded_result()
    {
        var store = new InMemoryStorageStore();
        await SeedContentAsync(store, "items", "order:a", new byte[] { 1 });
        await SeedContentAsync(store, "items", "order:b", new byte[] { 2 });

        await WithNodeAsync(
            StorageCompositionNodeTypes.Query,
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<StorageQueryOutcome>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new StorageQueryRequest
                {
                    KeyPrefix = "order:"
                }))).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull().Value;
                result.Count.ShouldBe(1);
                result.Records.ShouldBeEmpty();
            },
            Properties(
                ("collection", "items"),
                ("limit", 1),
                ("emitRecordsInResult", false)),
            store);
    }

    [Fact]
    public async Task Hosted_delete_returns_missing_as_a_normal_result()
    {
        var store = new InMemoryStorageStore();

        await WithNodeAsync(
            StorageCompositionNodeTypes.Delete,
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<StorageDeleteOutcome>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new StorageDeleteRequest
                {
                    Key = "missing"
                }))).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.IsError.ShouldBeFalse();
                result.Value.Found.ShouldBeFalse();
            },
            Properties(("collection", "items")),
            store);
    }

    [Fact]
    public async Task Missing_store_resource_reference_surfaces_factory_diagnostic()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                StorageCompositionNodeTypes.Put,
                Properties(("collection", "items"))),
            registry => registry.RegisterStoragePut());

        AssertPreparationFailure(host, StorageCompositionResourceNames.Store);
    }

    [Theory]
    [InlineData(StorageCompositionNodeTypes.Put, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(StorageCompositionNodeTypes.Get, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(StorageCompositionNodeTypes.Query, "limit", 0, "limit")]
    [InlineData(StorageCompositionNodeTypes.Query, "offset", -1, "offset")]
    [InlineData(StorageCompositionNodeTypes.Delete, "boundedCapacity", 0, "boundedCapacity")]
    public async Task Invalid_configuration_surfaces_factory_diagnostic(
        string nodeType,
        string optionName,
        int value,
        string expectedMessage)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [StorageCompositionResourceNames.Store] = "Resources.store",
            ["collection"] = "items",
            [optionName] = value
        };
        var store = new InMemoryStorageStore();
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(nodeType, properties, ["store"]),
            registry => RegisterAll(registry),
            configureRuntimeServices: context =>
                context.Services.AddExternalFluxFlowResource<IStorageStore>(
                    ApplicationAddress.Resource("store"),
                    store));

        AssertPreparationFailure(host, expectedMessage);
    }

    [Fact]
    public async Task Runtime_store_failure_is_normal_and_later_messages_continue()
    {
        var store = new InMemoryStorageStore { FailuresRemaining = 1 };

        await WithNodeAsync(
            StorageCompositionNodeTypes.Put,
            async (ports, _) =>
            {
                foreach (var key in new[] { "bad", "good" })
                {
                    var resultReceive = ports.ReceiveAsync<StoragePutOutcome>(
                        Output,
                        Timeout);
                    (await ports.SendAsync(Input, FlowMessage.Create(new StorageContentPutRequest
                    {
                        Key = key,
                        Content = FlowContent.FromBytes(new byte[] { 1 })
                    }))).IsAccepted.ShouldBeTrue();

                    var result = (await resultReceive).Message.ShouldNotBeNull();
                    result.IsError.ShouldBe(key == "bad");
                    if (key == "bad")
                    {
                        result.Error.ShouldNotBeNull().Code
                            .ShouldBe(StorageErrorCodeNames.PutFailed);
                    }
                    else
                    {
                        result.Value.Key.ShouldBe("good");
                    }
                }
            },
            Properties(("collection", "items")),
            store);
    }

    private static async Task WithNodeAsync(
        string nodeType,
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        object store,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[StorageCompositionResourceNames.Store] = "Resources.store";
        var resources = new List<string> { "store" };
        if (clock is not null)
        {
            componentProperties[StorageCompositionResourceNames.Clock] = "Resources.clock";
            resources.Add("clock");
        }

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(nodeType, componentProperties, resources),
            registry => RegisterAll(registry),
            configureRuntimeServices: context =>
            {
                switch (store)
                {
                    case IStorageStore direct:
                        context.Services.AddExternalFluxFlowResource<IStorageStore>(
                            ApplicationAddress.Resource("store"),
                            direct);
                        break;
                    case IStorageStoreFactory factory:
                        context.Services.AddExternalFluxFlowResource<IStorageStoreFactory>(
                            ApplicationAddress.Resource("store"),
                            factory);
                        break;
                    default:
                        throw new ArgumentException(
                            "Store must implement IStorageStore or IStorageStoreFactory.",
                            nameof(store));
                }

                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("clock"),
                        clock);
                }
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static CompositionNodeRegistry RegisterAll(CompositionNodeRegistry registry)
        => registry
            .RegisterStoragePut()
            .RegisterStorageGet()
            .RegisterStorageQuery()
            .RegisterStorageDelete();

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => new StorageComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static Dictionary<string, ResourceDesignMetadata> ResourcesByName(
        ComponentDesignMetadata metadata)
        => metadata.Resources.ToDictionary(resource => resource.Name.Value, StringComparer.Ordinal);

    private static void AssertTransformPorts(
        ComponentDesignMetadata metadata,
        string inputType,
        string outputType)
    {
        metadata.Ports.Count.ShouldBe(2);
        metadata.Ports[0].Name.Value.ShouldBe(StorageCompositionPortNames.Input);
        metadata.Ports[0].Direction.ShouldBe(PortDirection.Input);
        metadata.Ports[0].ValueType?.Value.ShouldBe(inputType);
        metadata.Ports[0].IsPrimary.ShouldBeTrue();
        metadata.Ports[1].Name.Value.ShouldBe(StorageCompositionPortNames.Output);
        metadata.Ports[1].Direction.ShouldBe(PortDirection.Output);
        metadata.Ports[1].ValueType?.Value.ShouldBe(outputType);
        metadata.Ports[1].IsPrimary.ShouldBeTrue();
    }

    private static void AssertResources(ComponentDesignMetadata metadata)
    {
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (StorageCompositionResourceNames.Store, 0, true, $"{nameof(IStorageStore)} or {nameof(IStorageStoreFactory)}"),
            (StorageCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
        ]);
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section).ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance).ShouldBe(importance);
        if (editor is null)
        {
            option.Attributes.ContainsKey(
                new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor)).ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor).ShouldBe(editor);
        }
    }

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static async Task SeedContentAsync(
        IStorageStore store,
        string collection,
        string key,
        byte[] bytes)
    {
        await using var node = new StoragePutNode(
            store,
            new StoragePutOptions { Collection = collection });
        var output = Link(node.Output);
        await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = key,
            Content = FlowContent.FromBytes(bytes, "application/octet-stream")
        }));
        (await output.ReceiveAsync().WaitAsync(Timeout)).IsError.ShouldBeFalse();
    }

    private sealed class InMemoryStorageStore : IStorageStore, IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string Collection, string Key), StorageRecord> _records = [];

        public int FailuresRemaining { get; set; }
        public int DisposeCount { get; private set; }

        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
            var collection = Required(request.Collection, "collection");
            var key = Required(request.Key, "key");
            lock (_gate)
            {
                _records.TryGetValue((collection, key), out var existing);
                var record = new StorageRecord
                {
                    Collection = collection,
                    Key = key,
                    Value = request.Value,
                    ContentType = request.ContentType,
                    Attributes = new Dictionary<string, string>(request.Attributes),
                    Version = (existing?.Version ?? 0) + 1,
                    StoredAt = DateTimeOffset.UtcNow,
                    ExpiresAt = request.ExpiresAt,
                    CorrelationId = request.CorrelationId
                };
                _records[(collection, key)] = record;
                return Task.FromResult(Copy(record));
            }
        }

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
            lock (_gate)
            {
                return Task.FromResult(_records.TryGetValue(
                    (Required(request.Collection, "collection"), Required(request.Key, "key")),
                    out var record)
                    ? Copy(record)
                    : null);
            }
        }

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
            var collection = Required(request.Collection, "collection");
            lock (_gate)
            {
                IReadOnlyList<StorageRecord> result = _records.Values
                    .Where(record => StringComparer.Ordinal.Equals(record.Collection, collection))
                    .Where(record => StorageQueryMatcher.IsMatch(record, request, DateTimeOffset.UtcNow))
                    .OrderBy(record => record.StoredAt)
                    .ThenBy(record => record.Key, StringComparer.Ordinal)
                    .Skip(request.Offset ?? 0)
                    .Take(request.Limit ?? int.MaxValue)
                    .Select(Copy)
                    .ToArray();
                return Task.FromResult(result);
            }
        }

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
            var collection = Required(request.Collection, "collection");
            var key = Required(request.Key, "key");
            lock (_gate)
            {
                var found = _records.Remove((collection, key), out var record);
                return Task.FromResult(new StorageResult
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Operation = "delete",
                    Collection = collection,
                    Key = key,
                    Succeeded = true,
                    Found = found,
                    Deleted = found,
                    Version = record?.Version
                });
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        private void ThrowIfFailing()
        {
            if (FailuresRemaining <= 0)
                return;
            FailuresRemaining--;
            throw new InvalidOperationException("Store failure.");
        }

        private static StorageRecord Copy(StorageRecord record)
            => record with
            {
                Attributes = new Dictionary<string, string>(record.Attributes)
            };

        private static string Required(string? value, string name)
            => string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"Storage request requires {name}.")
                : value.Trim();
    }

    private sealed class RecordingStorageStoreFactory(IStorageStore store) : IStorageStoreFactory
    {
        public int OpenCount { get; private set; }
        public StorageStoreContext? Context { get; private set; }

        public ValueTask<StorageStoreLease> OpenAsync(
            StorageStoreContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            Context = context;
            return ValueTask.FromResult(StorageStoreLease.Owned(store));
        }
    }
}
