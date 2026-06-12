using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Definitions;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class RoutingComponentDesignMetadataProviderTests
{
    [Fact]
    public void Metadata_SwitchListsAllStaticPorts()
    {
        var metadata = GetMetadata(RoutingComponentTypes.Switch);

        GetPortNames(metadata, PortDirection.Input).ShouldBe(
            [RoutingComponentPorts.Input]);
        GetPortNames(metadata, PortDirection.Output).ShouldBe(
            [
                RoutingComponentPorts.Result,
                RoutingComponentPorts.Routed,
                RoutingComponentPorts.Matched,
                RoutingComponentPorts.Default,
                RoutingComponentPorts.Errors
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Metadata_CorrelationListsSplitInputPorts()
    {
        var metadata = GetMetadata(RoutingComponentTypes.Correlation);

        GetPortNames(metadata, PortDirection.Input).ShouldBe(
            [
                RoutingComponentPorts.Input,
                RoutingComponentPorts.Request,
                RoutingComponentPorts.Response
            ],
            ignoreOrder: true);
        GetPortNames(metadata, PortDirection.Output).ShouldBe(
            [
                RoutingComponentPorts.Matched,
                RoutingComponentPorts.Timeouts,
                RoutingComponentPorts.Errors
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Metadata_CorrelationTimeoutDefaultMatchesOptions()
    {
        var metadata = GetMetadata(RoutingComponentTypes.Correlation);

        var timeout = metadata.Options.Single(option => option.Name == "timeoutMilliseconds");
        timeout.DefaultValue.ShouldBe(new CorrelationRoutingOptions().TimeoutMilliseconds);
    }

    [Fact]
    public void Metadata_JoinTimeoutDefaultMatchesOptions()
    {
        var metadata = GetMetadata(RoutingComponentTypes.Join);

        var timeout = metadata.Options.Single(option => option.Name == "timeoutMilliseconds");
        timeout.DefaultValue.ShouldBe(new JoinRoutingOptions().TimeoutMilliseconds);
    }

    private static ComponentDesignMetadata GetMetadata(NodeType type)
        => new RoutingComponentDesignMetadataProvider()
            .GetMetadata()
            .Single(metadata => metadata.Type == type);

    private static string[] GetPortNames(
        ComponentDesignMetadata metadata,
        PortDirection direction)
        => metadata.Ports
            .Where(port => port.Direction == direction)
            .Select(port => port.Name.Value)
            .ToArray();
}
