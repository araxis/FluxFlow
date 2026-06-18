using FluxFlow.Components.Assertions;
using FluxFlow.Components.Control;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations;
using FluxFlow.Components.FileSystem;
using FluxFlow.Components.Mapping;
using FluxFlow.Components.Metrics;
using FluxFlow.Components.Mqtt;
using FluxFlow.Components.Observability;
using FluxFlow.Components.Payloads;
using FluxFlow.Components.Projections;
using FluxFlow.Components.Routing;
using FluxFlow.Components.Serialization;
using FluxFlow.Components.Sessions;
using FluxFlow.Components.Sources;
using FluxFlow.Components.State;
using FluxFlow.Components.Storage;
using FluxFlow.Components.Timers;
using FluxFlow.Components.Validation;
using FluxFlow.Engine.Definitions;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentPackageDesignMetadataCoverageTests
{
    [Fact]
    public void Package_metadata_providers_compose_into_one_valid_catalog()
    {
        var providers = CreateProviders();

        var catalog = ComponentDesignMetadataCatalog.FromProviders(providers);

        catalog.All.Count.ShouldBe(ExpectedTypes.Count);
        foreach (var type in ExpectedTypes)
        {
            catalog.TryGet(type, out var metadata).ShouldBeTrue(type.Value);
            metadata.DisplayName.ShouldNotBeNullOrWhiteSpace();
            metadata.Category.ShouldNotBeNullOrWhiteSpace();
            metadata.Ports.ShouldNotBeEmpty(type.Value);
        }
    }

    [Fact]
    public void Package_metadata_covers_public_component_type_constants()
    {
        var providedTypes = CreateProviders()
            .SelectMany(provider => provider.GetMetadata())
            .Select(metadata => metadata.Type)
            .ToHashSet();

        var expectedTypes = ExpectedTypes.ToHashSet();
        providedTypes.Except(expectedTypes).ShouldBeEmpty();
        expectedTypes.Except(providedTypes).ShouldBeEmpty();
    }

    private static IReadOnlyList<IComponentDesignMetadataProvider> CreateProviders() =>
    [
        new AssertionsComponentDesignMetadataProvider(),
        new ControlComponentDesignMetadataProvider(),
        new ExpectationsComponentDesignMetadataProvider(),
        new FileSystemComponentDesignMetadataProvider(),
        new MappingComponentDesignMetadataProvider(),
        new MetricsComponentDesignMetadataProvider(),
        new MqttComponentDesignMetadataProvider(),
        new ObservabilityComponentDesignMetadataProvider(),
        new PayloadComponentDesignMetadataProvider(),
        new ProjectionsComponentDesignMetadataProvider(),
        new RoutingComponentDesignMetadataProvider(),
        new SerializationComponentDesignMetadataProvider(),
        new SessionsComponentDesignMetadataProvider(),
        new SourcesComponentDesignMetadataProvider(),
        new StateComponentDesignMetadataProvider(),
        new StorageComponentDesignMetadataProvider(),
        new TimerComponentDesignMetadataProvider(),
        new ValidationComponentDesignMetadataProvider()
    ];

    private static readonly IReadOnlyList<NodeType> ExpectedTypes =
    [
        AssertionsComponentTypes.Assert,
        ControlComponentTypes.Filter,
        ControlComponentTypes.When,
        ExpectationsComponentTypes.Expect,
        ExpectationsComponentTypes.Guard,
        FileSystemComponentTypes.DirectoryEnumerate,
        FileSystemComponentTypes.FileRead,
        FileSystemComponentTypes.FileWatch,
        FileSystemComponentTypes.FileWrite,
        MappingComponentTypes.Mapper,
        MetricsComponentTypes.Aggregate,
        MqttComponentTypes.Connection,
        MqttComponentTypes.Publish,
        MqttComponentTypes.Subscribe,
        ObservabilityComponentTypes.Counter,
        ObservabilityComponentTypes.Logger,
        ObservabilityComponentTypes.Metrics,
        PayloadComponentTypes.Inspect,
        ProjectionsComponentTypes.EventProjection,
        RoutingComponentTypes.Correlation,
        RoutingComponentTypes.Fork,
        RoutingComponentTypes.Join,
        RoutingComponentTypes.Merge,
        RoutingComponentTypes.Switch,
        RoutingComponentTypes.Window,
        SerializationComponentTypes.Base64Decode,
        SerializationComponentTypes.Base64Encode,
        SerializationComponentTypes.JsonParse,
        SerializationComponentTypes.JsonStringify,
        SerializationComponentTypes.TextDecode,
        SerializationComponentTypes.TextEncode,
        SessionsComponentTypes.Query,
        SessionsComponentTypes.Recorder,
        SessionsComponentTypes.Replay,
        SourcesComponentTypes.Generated,
        SourcesComponentTypes.Sequence,
        StateComponentTypes.Reducer,
        StorageComponentTypes.Delete,
        StorageComponentTypes.Get,
        StorageComponentTypes.Put,
        StorageComponentTypes.Query,
        StorageComponentTypes.Store,
        TimerComponentTypes.Debounce,
        TimerComponentTypes.Delay,
        TimerComponentTypes.Interval,
        TimerComponentTypes.Schedule,
        TimerComponentTypes.Throttle,
        ValidationComponentTypes.JsonSchemaValidator
    ];
}
