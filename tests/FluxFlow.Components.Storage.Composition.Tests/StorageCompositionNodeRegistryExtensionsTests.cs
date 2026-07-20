using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Composition.Tests;

public sealed class StorageCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Register_storage_nodes_registers_canonical_metadata()
    {
        var registry = RegisterAll(new CompositionNodeRegistry());

        var put = registry.Registrations[StorageCompositionNodeTypes.Put];
        put.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageContentPutRequest));
        put.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<StoragePutOutcome>));

        var get = registry.Registrations[StorageCompositionNodeTypes.Get];
        get.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageGetRequest));
        get.Outputs.Keys.ShouldBe([StorageCompositionPortNames.Output]);
        get.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<StorageGetOutcome>));

        var query = registry.Registrations[StorageCompositionNodeTypes.Query];
        query.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageQueryRequest));
        query.Outputs.Keys.ShouldBe([StorageCompositionPortNames.Output]);
        query.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<StorageQueryOutcome>));

        var delete = registry.Registrations[StorageCompositionNodeTypes.Delete];
        delete.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<StorageDeleteOutcome>));
    }

    [Fact]
    public void Typed_compatibility_registrations_preserve_released_contracts()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterStoragePutResult("storage.put.typed")
            .RegisterStorageGetResultBranches("storage.get.typed")
            .RegisterStorageQueryRecordOutputs("storage.query.typed")
            .RegisterStorageDeleteResult("storage.delete.typed");

        registry.Registrations["storage.put.typed"]
            .Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StoragePutRequest));
        registry.Registrations["storage.get.typed"].Outputs.Keys.ShouldBe([
            StorageCompositionPortNames.Output,
            StorageCompositionPortNames.Found,
            StorageCompositionPortNames.NotFound
        ], ignoreOrder: false);
        registry.Registrations["storage.query.typed"]
            .Outputs[StorageCompositionPortNames.Records].MessageType
            .ShouldBe(typeof(StorageRecord));
        registry.Registrations["storage.delete.typed"]
            .Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StorageResult));
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
            "FlowResult<StoragePutOutcome>");
        AssertTransformPorts(
            metadata[StorageCompositionNodeTypes.Get],
            nameof(StorageGetRequest),
            "FlowResult<StorageGetOutcome>");
        AssertTransformPorts(
            metadata[StorageCompositionNodeTypes.Query],
            nameof(StorageQueryRequest),
            "FlowResult<StorageQueryOutcome>");
        AssertTransformPorts(
            metadata[StorageCompositionNodeTypes.Delete],
            nameof(StorageDeleteRequest),
            "FlowResult<StorageDeleteOutcome>");
    }

    [Fact]
    public void Design_metadata_provider_omits_typed_branch_options()
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
        AttributeValue(query.Attributes, "omittedOptions").ShouldBe("emitRecordOutputs");
        delete.Options.Select(option => option.Name.Value).ShouldBe([
            "collection",
            "boundedCapacity"
        ], ignoreOrder: false);
        AttributeValue(delete.Attributes, "omittedOptions").ShouldBe("emitMissingAsResult");
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
            async descriptor =>
            {
                descriptor.Errors.ShouldBeNull();
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageContentPutRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<FlowResult<StoragePutOutcome>>>();
                var results = Link(output.Source);
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var message = FlowMessage.Create(
                    new StorageContentPutRequest
                    {
                        Key = "a",
                        Content = FlowContent.FromBytes(
                            new byte[] { 0x00, 0xFF },
                            "application/octet-stream")
                    },
                    new CorrelationId("put-1"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.CausationId.ShouldBe(message.MessageId);
                result.Payload.Timestamp.ShouldBe(timestamp);
                result.Payload.Kind.ShouldBe(StorageResultKinds.PutStored);
                result.Payload.Value.ShouldNotBeNull().Collection.ShouldBe("items");
                result.Payload.Value.Record.ShouldNotBeNull()
                    .Content.OriginalBytes.AsSpan().ToArray().ShouldBe([0x00, 0xFF]);
                (await events.ReceiveAsync().WaitAsync(Timeout)).Name
                    .ShouldBe(StorageDiagnosticNames.PutStored);
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Resource(StorageCompositionResourceNames.Clock, "fixed")
                .Configure("collection", "items")
                .Configure("mode", StorageWriteMode.Create)
                .Configure("boundedCapacity", 8),
            services =>
            {
                services.AddKeyedSingleton<IStorageStore>("store", store);
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });
    }

    [Fact]
    public async Task Hosted_put_resolves_factory_and_disposes_owned_lease()
    {
        var store = new InMemoryStorageStore();
        var factory = new RecordingStorageStoreFactory(store);

        await WithNodeAsync(
            StorageCompositionNodeTypes.Put,
            async descriptor =>
            {
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageContentPutRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<FlowResult<StoragePutOutcome>>>();
                var results = Link(output.Source);
                await input.Target.SendAsync(FlowMessage.Create(new StorageContentPutRequest
                {
                    Key = "a",
                    Content = FlowContent.FromBytes(new byte[] { 1 })
                }));
                (await results.ReceiveAsync().WaitAsync(Timeout)).Payload.IsError.ShouldBeFalse();
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "factory")
                .Configure("collection", "items"),
            services => services.AddKeyedSingleton<IStorageStoreFactory>("factory", factory));

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
            async descriptor =>
            {
                descriptor.Outputs.Keys.ShouldBe([StorageCompositionPortNames.Output]);
                descriptor.Errors.ShouldBeNull();
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageGetRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<FlowResult<StorageGetOutcome>>>();
                var results = Link(output.Source);
                await input.Target.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));
                await input.Target.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "missing" }));

                var found = (await results.ReceiveAsync().WaitAsync(Timeout)).Payload;
                found.Kind.ShouldBe(StorageResultKinds.GetFound);
                found.Value.ShouldNotBeNull().Record.ShouldNotBeNull()
                    .Content.OriginalBytes.AsSpan().ToArray().ShouldBe([1, 2]);
                var missing = (await results.ReceiveAsync().WaitAsync(Timeout)).Payload;
                missing.Kind.ShouldBe(StorageResultKinds.GetNotFound);
                missing.IsError.ShouldBeFalse();
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Configure("collection", "items"),
            services => services.AddKeyedSingleton<IStorageStore>("store", store));
    }

    [Fact]
    public async Task Hosted_query_returns_one_bounded_result()
    {
        var store = new InMemoryStorageStore();
        await SeedContentAsync(store, "items", "order:a", new byte[] { 1 });
        await SeedContentAsync(store, "items", "order:b", new byte[] { 2 });

        await WithNodeAsync(
            StorageCompositionNodeTypes.Query,
            async descriptor =>
            {
                descriptor.Outputs.Keys.ShouldBe([StorageCompositionPortNames.Output]);
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageQueryRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<FlowResult<StorageQueryOutcome>>>();
                var results = Link(output.Source);
                await input.Target.SendAsync(FlowMessage.Create(new StorageQueryRequest
                {
                    KeyPrefix = "order:"
                }));

                var result = (await results.ReceiveAsync().WaitAsync(Timeout)).Payload;
                result.Kind.ShouldBe(StorageResultKinds.QueryCompleted);
                result.Value.ShouldNotBeNull().Count.ShouldBe(1);
                result.Value.Records.ShouldBeEmpty();
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Configure("collection", "items")
                .Configure("limit", 1)
                .Configure("emitRecordsInResult", false),
            services => services.AddKeyedSingleton<IStorageStore>("store", store));
    }

    [Fact]
    public async Task Hosted_delete_returns_missing_even_when_legacy_suppression_is_configured()
    {
        var store = new InMemoryStorageStore();

        await WithNodeAsync(
            StorageCompositionNodeTypes.Delete,
            async descriptor =>
            {
                descriptor.Errors.ShouldBeNull();
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageDeleteRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<FlowResult<StorageDeleteOutcome>>>();
                var results = Link(output.Source);
                await input.Target.SendAsync(FlowMessage.Create(new StorageDeleteRequest
                {
                    Key = "missing"
                }));

                var result = (await results.ReceiveAsync().WaitAsync(Timeout)).Payload;
                result.Kind.ShouldBe(StorageResultKinds.DeleteNotFound);
                result.IsError.ShouldBeFalse();
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Configure("collection", "items")
                .Configure("emitMissingAsResult", false),
            services => services.AddKeyedSingleton<IStorageStore>("store", store));
    }

    [Fact]
    public async Task Typed_compatibility_node_keeps_error_and_branch_ports()
    {
        var store = new InMemoryStorageStore();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IStorageStore>("store", store);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "get",
                    "storage.get.typed",
                    node => node
                        .Resource(StorageCompositionResourceNames.Store, "store")
                        .Configure("collection", "items")))
                .Build())
            .RegisterNodes(registry => registry.RegisterStorageGetResultBranches("storage.get.typed"))
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);
        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem().Descriptor;

        descriptor.Errors.ShouldNotBeNull();
        descriptor.Outputs.Keys.ShouldBe([
            StorageCompositionPortNames.Output,
            StorageCompositionPortNames.Found,
            StorageCompositionPortNames.NotFound
        ], ignoreOrder: false);
    }

    [Fact]
    public async Task Missing_store_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "storage",
                    StorageCompositionNodeTypes.Put,
                    node => node.Configure("collection", "items")))
                .Build())
            .RegisterNodes(registry => registry.RegisterStoragePut())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);
        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(StorageCompositionResourceNames.Store));
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
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IStorageStore>("store", new InMemoryStorageStore());
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "storage",
                    nodeType,
                    node => node
                        .Resource(StorageCompositionResourceNames.Store, "store")
                        .Configure("collection", "items")
                        .Configure(optionName, value)))
                .Build())
            .RegisterNodes(registry => RegisterAll(registry))
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);
        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Runtime_store_failure_is_normal_and_later_messages_continue()
    {
        var store = new InMemoryStorageStore { FailuresRemaining = 1 };

        await WithNodeAsync(
            StorageCompositionNodeTypes.Put,
            async descriptor =>
            {
                descriptor.Errors.ShouldBeNull();
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageContentPutRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<FlowResult<StoragePutOutcome>>>();
                var results = Link(output.Source);
                foreach (var key in new[] { "bad", "good" })
                {
                    await input.Target.SendAsync(FlowMessage.Create(new StorageContentPutRequest
                    {
                        Key = key,
                        Content = FlowContent.FromBytes(new byte[] { 1 })
                    }));
                }

                var failure = (await results.ReceiveAsync().WaitAsync(Timeout)).Payload;
                failure.Kind.ShouldBe(StorageResultKinds.PutFailed);
                failure.Error.ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.PutFailed);
                var success = (await results.ReceiveAsync().WaitAsync(Timeout)).Payload;
                success.Kind.ShouldBe(StorageResultKinds.PutStored);
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Configure("collection", "items"),
            services => services.AddKeyedSingleton<IStorageStore>("store", store));
    }

    private static async Task WithNodeAsync(
        string nodeType,
        Func<ComposedNode, Task> run,
        Action<NodeDefinitionBuilder> configureNode,
        Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node("storage", nodeType, configureNode))
                .Build())
            .RegisterNodes(registry => RegisterAll(registry))
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);
        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem().Descriptor;
        await run(descriptor);
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

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
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
        await using var node = new FlowContentStoragePutNode(
            store,
            new StoragePutOptions { Collection = collection });
        var output = Link(node.Output);
        await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = key,
            Content = FlowContent.FromBytes(bytes, "application/octet-stream")
        }));
        (await output.ReceiveAsync().WaitAsync(Timeout)).Payload.IsError.ShouldBeFalse();
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
