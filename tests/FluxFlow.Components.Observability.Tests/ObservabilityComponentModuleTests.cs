using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

public sealed class ObservabilityComponentModuleTests
{
    [Fact]
    public void RegisterObservabilityComponents_AddsObserverFactories()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterObservabilityComponents();

        registry.TryGetFactory(ObservabilityComponentTypes.Counter, out var counterFactory).ShouldBeTrue();
        counterFactory.ShouldNotBeNull();
        registry.TryGetFactory(ObservabilityComponentTypes.Logger, out var loggerFactory).ShouldBeTrue();
        loggerFactory.ShouldNotBeNull();
        registry.TryGetFactory(ObservabilityComponentTypes.Metrics, out var metricsFactory).ShouldBeTrue();
        metricsFactory.ShouldNotBeNull();
    }
}
