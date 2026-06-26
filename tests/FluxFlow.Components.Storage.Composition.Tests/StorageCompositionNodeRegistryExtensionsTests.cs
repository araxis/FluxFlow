using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Composition.Tests;

public sealed class StorageCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Register_storage_nodes_registers_typed_metadata()
    {
        var registry = RegisterAll(new CompositionNodeRegistry());

        var put = registry.Registrations[StorageCompositionNodeTypes.Put];
        put.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StoragePutRequest));
        put.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StorageResult));

        var get = registry.Registrations[StorageCompositionNodeTypes.Get];
        get.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageGetRequest));
        get.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StorageResult));
        get.Outputs[StorageCompositionPortNames.Found].MessageType
            .ShouldBe(typeof(StorageResult));
        get.Outputs[StorageCompositionPortNames.NotFound].MessageType
            .ShouldBe(typeof(StorageResult));

        var query = registry.Registrations[StorageCompositionNodeTypes.Query];
        query.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageQueryRequest));
        query.Outputs[StorageCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StorageQueryResult));
        query.Outputs[StorageCompositionPortNames.Records].MessageType
            .ShouldBe(typeof(StorageRecord));

        var delete = registry.Registrations[StorageCompositionNodeTypes.Delete];
        delete.Inputs[StorageCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(StorageDeleteRequest));
        delete.Outputs[StorageCompositionPortNames.Output].MessageType
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
            item.Category.ShouldBe("Storage");
            item.SuggestedEditorWidth.ShouldBe(460);
            item.Options.ShouldNotContain(option =>
                option.Name == StorageCompositionResourceNames.Store ||
                option.Name == StorageCompositionResourceNames.Clock);
            AssertResources(item);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_storage_ports()
    {
        var metadata = DesignMetadataByType();

        AssertTransformPorts<StoragePutRequest, StorageResult>(
            metadata[StorageCompositionNodeTypes.Put]);
        AssertGetPorts(metadata[StorageCompositionNodeTypes.Get]);
        AssertQueryPorts(metadata[StorageCompositionNodeTypes.Query]);
        AssertTransformPorts<StorageDeleteRequest, StorageResult>(
            metadata[StorageCompositionNodeTypes.Delete]);
    }

    [Fact]
    public void Design_metadata_provider_describes_storage_options()
    {
        var metadata = DesignMetadataByType();
        var putDefaults = new StoragePutOptions();
        var getDefaults = new StorageGetOptions();
        var queryDefaults = new StorageQueryOptions();
        var deleteDefaults = new StorageDeleteOptions();

        AssertOptionNames(
            metadata[StorageCompositionNodeTypes.Put],
            "collection",
            "mode",
            "emitStoredRecord",
            "boundedCapacity");
        AssertOption(
            metadata[StorageCompositionNodeTypes.Put],
            "collection",
            OptionValueKind.Text);
        var mode = AssertOption(
            metadata[StorageCompositionNodeTypes.Put],
            "mode",
            OptionValueKind.Enum,
            putDefaults.Mode.ToString());
        mode.Choices.Select(choice => choice.Value).ShouldBe([
            nameof(StorageWriteMode.Upsert),
            nameof(StorageWriteMode.Create),
            nameof(StorageWriteMode.Replace)
        ], ignoreOrder: false);
        AssertOption(
            metadata[StorageCompositionNodeTypes.Put],
            "emitStoredRecord",
            OptionValueKind.Boolean,
            putDefaults.EmitStoredRecord);
        AssertOption(
            metadata[StorageCompositionNodeTypes.Put],
            "boundedCapacity",
            OptionValueKind.Number,
            putDefaults.BoundedCapacity,
            min: 1);

        AssertOptionNames(
            metadata[StorageCompositionNodeTypes.Get],
            "collection",
            "includeExpired",
            "boundedCapacity");
        AssertOption(
            metadata[StorageCompositionNodeTypes.Get],
            "includeExpired",
            OptionValueKind.Boolean,
            getDefaults.IncludeExpired);
        AssertOption(
            metadata[StorageCompositionNodeTypes.Get],
            "boundedCapacity",
            OptionValueKind.Number,
            getDefaults.BoundedCapacity,
            min: 1);

        AssertOptionNames(
            metadata[StorageCompositionNodeTypes.Query],
            "collection",
            "includeExpired",
            "offset",
            "limit",
            "emitRecordsInResult",
            "emitRecordOutputs",
            "boundedCapacity");
        AssertOption(
            metadata[StorageCompositionNodeTypes.Query],
            "offset",
            OptionValueKind.Number,
            queryDefaults.Offset,
            min: 0);
        AssertOption(
            metadata[StorageCompositionNodeTypes.Query],
            "limit",
            OptionValueKind.Number,
            queryDefaults.Limit,
            min: 1);
        AssertOption(
            metadata[StorageCompositionNodeTypes.Query],
            "emitRecordsInResult",
            OptionValueKind.Boolean,
            queryDefaults.EmitRecordsInResult);
        AssertOption(
            metadata[StorageCompositionNodeTypes.Query],
            "emitRecordOutputs",
            OptionValueKind.Boolean,
            queryDefaults.EmitRecordOutputs);

        AssertOptionNames(
            metadata[StorageCompositionNodeTypes.Delete],
            "collection",
            "emitMissingAsResult",
            "boundedCapacity");
        AssertOption(
            metadata[StorageCompositionNodeTypes.Delete],
            "emitMissingAsResult",
            OptionValueKind.Boolean,
            deleteDefaults.EmitMissingAsResult);
        AssertOption(
            metadata[StorageCompositionNodeTypes.Delete],
            "boundedCapacity",
            OptionValueKind.Number,
            deleteDefaults.BoundedCapacity,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new StorageComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(4);
        catalog.TryGet(
            new ComponentType(StorageCompositionNodeTypes.Put),
            out var putMetadata).ShouldBeTrue();
        putMetadata.ShouldNotBeNull().DisplayName.ShouldBe("Storage Put");
        catalog.TryGet(
            new ComponentType(StorageCompositionNodeTypes.Query),
            out var queryMetadata).ShouldBeTrue();
        queryMetadata.ShouldNotBeNull().DisplayName.ShouldBe("Storage Query");
    }

    [Fact]
    public async Task Hosted_put_resolves_store_binds_options_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-20T10:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new InMemoryStorageStore();

        await WithNodeAsync(
            StorageCompositionNodeTypes.Put,
            async descriptor =>
            {
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StoragePutRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<StorageResult>>();
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var results = Link(output.Source);
                var message = FlowMessage.Create(
                    new StoragePutRequest { Key = "a", Value = "one" },
                    new CorrelationId("put-1"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.Payload.Timestamp.ShouldBe(timestamp);
                result.Payload.Operation.ShouldBe("put");
                result.Payload.Collection.ShouldBe("items");
                result.Payload.Key.ShouldBe("a");
                result.Payload.Record.ShouldNotBeNull().Value.ShouldBe("one");
                store.RecordCount.ShouldBe(1);

                var @event = await events.ReceiveAsync().WaitAsync(Timeout);
                @event.Name.ShouldBe(StorageDiagnosticNames.PutStored);
                @event.Timestamp.ShouldBe(timestamp);
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Resource(StorageCompositionResourceNames.Clock, "fixed")
                .Configure("collection", "items")
                .Configure("mode", StorageWriteMode.Create)
                .Configure("emitStoredRecord", true)
                .Configure("boundedCapacity", 8),
            services =>
            {
                services.AddKeyedSingleton<IStorageStore>("store", store);
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });
    }

    [Fact]
    public async Task Hosted_get_emits_output_found_and_not_found_branches()
    {
        var store = new InMemoryStorageStore();
        await SeedAsync(store, "items", "a", "one");

        await WithNodeAsync(
            StorageCompositionNodeTypes.Get,
            async descriptor =>
            {
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageGetRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<StorageResult>>();
                var found = descriptor.Outputs[StorageCompositionPortNames.Found]
                    .ShouldBeOfType<CompositionOutputPort<StorageResult>>();
                var notFound = descriptor.Outputs[StorageCompositionPortNames.NotFound]
                    .ShouldBeOfType<CompositionOutputPort<StorageResult>>();
                var outputs = Link(output.Source);
                var foundResults = Link(found.Source);
                var notFoundResults = Link(notFound.Source);
                var existing = FlowMessage.Create(
                    new StorageGetRequest { Key = "a" },
                    new CorrelationId("get-found"));
                var missing = FlowMessage.Create(
                    new StorageGetRequest { Key = "missing" },
                    new CorrelationId("get-missing"));

                (await input.Target.SendAsync(existing).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(missing).WaitAsync(Timeout)).ShouldBeTrue();

                var foundOutput = await outputs.ReceiveAsync().WaitAsync(Timeout);
                var missingOutput = await outputs.ReceiveAsync().WaitAsync(Timeout);
                var foundBranch = await foundResults.ReceiveAsync().WaitAsync(Timeout);
                var missingBranch = await notFoundResults.ReceiveAsync().WaitAsync(Timeout);

                foundOutput.CorrelationId.ShouldBe(existing.CorrelationId);
                foundOutput.Payload.Found.ShouldBeTrue();
                foundBranch.CorrelationId.ShouldBe(existing.CorrelationId);
                foundBranch.Payload.Record.ShouldNotBeNull().Value.ShouldBe("one");

                missingOutput.CorrelationId.ShouldBe(missing.CorrelationId);
                missingOutput.Payload.Found.ShouldBeFalse();
                missingBranch.CorrelationId.ShouldBe(missing.CorrelationId);
                missingBranch.Payload.Key.ShouldBe("missing");
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Configure("collection", "items")
                .Configure("boundedCapacity", 8),
            services => services.AddKeyedSingleton<IStorageStore>("store", store));
    }

    [Fact]
    public async Task Hosted_query_binds_options_and_fans_records()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-20T11:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var store = new InMemoryStorageStore();
        await SeedAsync(store, "items", "order:a", "one");
        await SeedAsync(store, "items", "order:b", "two");

        await WithNodeAsync(
            StorageCompositionNodeTypes.Query,
            async descriptor =>
            {
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageQueryRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<StorageQueryResult>>();
                var records = descriptor.Outputs[StorageCompositionPortNames.Records]
                    .ShouldBeOfType<CompositionOutputPort<StorageRecord>>();
                var results = Link(output.Source);
                var recordOutputs = Link(records.Source);
                var message = FlowMessage.Create(
                    new StorageQueryRequest { KeyPrefix = "order:" },
                    new CorrelationId("query-1"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                var record = await recordOutputs.ReceiveAsync().WaitAsync(Timeout);

                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.Payload.Timestamp.ShouldBe(timestamp);
                result.Payload.Collection.ShouldBe("items");
                result.Payload.Count.ShouldBe(1);
                result.Payload.Records.ShouldBeEmpty();
                record.CorrelationId.ShouldBe(message.CorrelationId);
                record.Payload.Key.ShouldBe("order:a");
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Resource(StorageCompositionResourceNames.Clock, "fixed")
                .Configure("collection", "items")
                .Configure("limit", 1)
                .Configure("emitRecordsInResult", false)
                .Configure("emitRecordOutputs", true),
            services =>
            {
                services.AddKeyedSingleton<IStorageStore>("store", store);
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });
    }

    [Fact]
    public async Task Hosted_delete_honors_missing_result_option()
    {
        var store = new InMemoryStorageStore();
        await SeedAsync(store, "items", "present", "one");

        await WithNodeAsync(
            StorageCompositionNodeTypes.Delete,
            async descriptor =>
            {
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StorageDeleteRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<StorageResult>>();
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var results = Link(output.Source);
                var missing = FlowMessage.Create(
                    new StorageDeleteRequest { Key = "missing" },
                    new CorrelationId("delete-missing"));
                var present = FlowMessage.Create(
                    new StorageDeleteRequest { Key = "present" },
                    new CorrelationId("delete-present"));

                (await input.Target.SendAsync(missing).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(present).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.CorrelationId.ShouldBe(present.CorrelationId);
                result.Payload.Key.ShouldBe("present");
                result.Payload.Deleted.ShouldBeTrue();

                var missingEvent = await events.ReceiveAsync().WaitAsync(Timeout);
                var deletedEvent = await events.ReceiveAsync().WaitAsync(Timeout);
                missingEvent.Name.ShouldBe(StorageDiagnosticNames.DeleteMissing);
                deletedEvent.Name.ShouldBe(StorageDiagnosticNames.DeleteDeleted);
            },
            node => node
                .Resource(StorageCompositionResourceNames.Store, "store")
                .Configure("collection", "items")
                .Configure("emitMissingAsResult", false),
            services => services.AddKeyedSingleton<IStorageStore>("store", store));
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
            diagnostic.Message.Contains(
                StorageCompositionResourceNames.Store,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(StorageCompositionNodeTypes.Put, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(StorageCompositionNodeTypes.Get, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(StorageCompositionNodeTypes.Query, "boundedCapacity", 0, "boundedCapacity")]
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
    public async Task Runtime_store_failure_emits_error_and_later_messages_continue()
    {
        var store = new InMemoryStorageStore
        {
            FailuresRemaining = 1
        };

        await WithNodeAsync(
            StorageCompositionNodeTypes.Put,
            async descriptor =>
            {
                var input = descriptor.Inputs[StorageCompositionPortNames.Input]
                    .ShouldBeOfType<CompositionInputPort<StoragePutRequest>>();
                var output = descriptor.Outputs[StorageCompositionPortNames.Output]
                    .ShouldBeOfType<CompositionOutputPort<StorageResult>>();
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var results = Link(output.Source);
                var bad = FlowMessage.Create(
                    new StoragePutRequest { Key = "bad", Value = "bad" },
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    new StoragePutRequest { Key = "good", Value = "ok" },
                    new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var result = await results.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(StorageErrorCodes.PutFailed);
                error.CorrelationId.ShouldBe(bad.CorrelationId);
                result.CorrelationId.ShouldBe(good.CorrelationId);
                result.Payload.Key.ShouldBe("good");
                result.Payload.Succeeded.ShouldBeTrue();
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
                .Workflow("main", workflow => workflow.Node(
                    "storage",
                    nodeType,
                    configureNode))
                .Build())
            .RegisterNodes(registry => RegisterAll(registry))
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;

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

    private static void AssertTransformPorts<TInput, TOutput>(
        ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(StorageCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType.ShouldBe(typeof(TInput).Name);
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(StorageCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType.ShouldBe(typeof(TOutput).Name);
        output.IsPrimary.ShouldBeTrue();
    }

    private static void AssertGetPorts(ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(4);

        metadata.Ports[0].Name.Value.ShouldBe(StorageCompositionPortNames.Input);
        metadata.Ports[0].ValueType.ShouldBe(nameof(StorageGetRequest));
        metadata.Ports[0].Direction.ShouldBe(PortDirection.Input);
        metadata.Ports[0].IsPrimary.ShouldBeTrue();

        AssertOutputPort(
            metadata.Ports[1],
            StorageCompositionPortNames.Output,
            nameof(StorageResult),
            order: 1,
            isPrimary: true);
        AssertOutputPort(
            metadata.Ports[2],
            StorageCompositionPortNames.Found,
            nameof(StorageResult),
            order: 2);
        AssertOutputPort(
            metadata.Ports[3],
            StorageCompositionPortNames.NotFound,
            nameof(StorageResult),
            order: 3);
    }

    private static void AssertQueryPorts(ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(3);

        metadata.Ports[0].Name.Value.ShouldBe(StorageCompositionPortNames.Input);
        metadata.Ports[0].ValueType.ShouldBe(nameof(StorageQueryRequest));
        metadata.Ports[0].Direction.ShouldBe(PortDirection.Input);
        metadata.Ports[0].IsPrimary.ShouldBeTrue();

        AssertOutputPort(
            metadata.Ports[1],
            StorageCompositionPortNames.Output,
            nameof(StorageQueryResult),
            order: 1,
            isPrimary: true);
        AssertOutputPort(
            metadata.Ports[2],
            StorageCompositionPortNames.Records,
            nameof(StorageRecord),
            order: 2);
    }

    private static void AssertOutputPort(
        PortDesignMetadata port,
        string name,
        string valueType,
        int order,
        bool isPrimary = false)
    {
        port.Name.Value.ShouldBe(name);
        port.Direction.ShouldBe(PortDirection.Output);
        port.Order.ShouldBe(order);
        port.ValueType.ShouldBe(valueType);
        port.IsPrimary.ShouldBe(isPrimary);
    }

    private static void AssertOptionNames(
        ComponentDesignMetadata metadata,
        params string[] names)
        => metadata.Options.Select(option => option.Name)
            .ShouldBe(names, ignoreOrder: false);

    private static OptionDesignMetadata AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue = null,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
        return option;
    }

    private static void AssertResources(ComponentDesignMetadata metadata)
    {
        metadata.Resources.Select(resource => (
            resource.Name,
            resource.Order,
            resource.IsRequired,
            resource.ValueType)).ShouldBe([
            (StorageCompositionResourceNames.Store, 0, true, nameof(IStorageStore)),
            (StorageCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
        ]);
    }

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

    private static Task SeedAsync(
        InMemoryStorageStore store,
        string collection,
        string key,
        object? value)
        => store.PutAsync(new StoragePutRequest
        {
            Collection = collection,
            Key = key,
            Value = value
        });

    private sealed class InMemoryStorageStore : IStorageStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string Collection, string Key), StorageRecord> _records = [];

        public int FailuresRemaining { get; set; }
        public int RecordCount
        {
            get
            {
                lock (_gate)
                {
                    return _records.Count;
                }
            }
        }

        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();

            var collection = Required(request.Collection, "collection");
            var key = Required(request.Key, "key");
            lock (_gate)
            {
                _records.TryGetValue((collection, key), out var existing);
                var mode = request.Mode ?? StorageWriteMode.Upsert;
                if (mode == StorageWriteMode.Create && existing is not null)
                {
                    throw new InvalidOperationException("Record already exists.");
                }

                if (mode == StorageWriteMode.Replace && existing is null)
                {
                    throw new InvalidOperationException("Record does not exist.");
                }

                var record = new StorageRecord
                {
                    Collection = collection,
                    Key = key,
                    Value = request.Value,
                    ContentType = request.ContentType,
                    Attributes = CopyAttributes(request.Attributes),
                    Version = (existing?.Version ?? 0) + 1,
                    StoredAt = DateTimeOffset.UtcNow,
                    ExpiresAt = request.ExpiresAt,
                    CorrelationId = request.CorrelationId
                };
                _records[(collection, key)] = record;
                return Task.FromResult(CopyRecord(record));
            }
        }

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();

            var collection = Required(request.Collection, "collection");
            var key = Required(request.Key, "key");
            lock (_gate)
            {
                return Task.FromResult(_records.TryGetValue((collection, key), out var record)
                    ? CopyRecord(record)
                    : null);
            }
        }

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();

            var collection = Required(request.Collection, "collection");
            lock (_gate)
            {
                var records = _records.Values
                    .Where(record => StringComparer.Ordinal.Equals(record.Collection, collection))
                    .Where(record => StorageQueryMatcher.IsMatch(record, request, DateTimeOffset.UtcNow))
                    .OrderBy(record => record.StoredAt)
                    .ThenBy(record => record.Key, StringComparer.Ordinal)
                    .Skip(request.Offset ?? 0)
                    .Take(request.Limit ?? int.MaxValue)
                    .Select(CopyRecord)
                    .ToArray();

                return Task.FromResult<IReadOnlyList<StorageRecord>>(records);
            }
        }

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
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
                    Record = record is null ? null : CopyRecord(record),
                    Version = record?.Version,
                    CorrelationId = request.CorrelationId,
                    Attributes = record is null ? [] : CopyAttributes(record.Attributes)
                });
            }
        }

        private void ThrowIfFailing()
        {
            if (FailuresRemaining <= 0)
            {
                return;
            }

            FailuresRemaining--;
            throw new InvalidOperationException("Store failure.");
        }

        private static string Required(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Storage request requires {name}.");
            }

            return value.Trim();
        }

        private static StorageRecord CopyRecord(StorageRecord record)
            => record with
            {
                Attributes = CopyAttributes(record.Attributes)
            };

        private static Dictionary<string, string> CopyAttributes(
            Dictionary<string, string>? source)
            => source is null
                ? []
                : new Dictionary<string, string>(source, StringComparer.Ordinal);
    }
}
