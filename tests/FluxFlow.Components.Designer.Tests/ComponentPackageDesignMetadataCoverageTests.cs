using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mqtt;
using FluxFlow.Components.Routing;
using FluxFlow.Components.Sources;
using FluxFlow.Components.Storage;
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
        new MqttComponentDesignMetadataProvider(),
        new RoutingComponentDesignMetadataProvider(),
        new SourcesComponentDesignMetadataProvider(),
        new StorageComponentDesignMetadataProvider(),
    ];

    private static readonly IReadOnlyList<NodeType> ExpectedTypes =
    [
        MqttComponentTypes.Connection,
        MqttComponentTypes.Publish,
        MqttComponentTypes.Subscribe,
        RoutingComponentTypes.Correlation,
        RoutingComponentTypes.Fork,
        RoutingComponentTypes.Join,
        RoutingComponentTypes.Merge,
        RoutingComponentTypes.Switch,
        RoutingComponentTypes.Window,
        SourcesComponentTypes.Generated,
        SourcesComponentTypes.Sequence,
        StorageComponentTypes.Delete,
        StorageComponentTypes.Get,
        StorageComponentTypes.Put,
        StorageComponentTypes.Query,
        StorageComponentTypes.Store,
    ];
}
