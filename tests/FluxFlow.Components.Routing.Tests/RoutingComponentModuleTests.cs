using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class RoutingComponentModuleTests
{
    [Fact]
    public void RegisterRoutingComponents_AddsSwitchFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(new RecordingExpressionEngine());

        registry.TryGetFactory(RoutingComponentTypes.Switch, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterRoutingComponents_AddsCorrelationFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(new RecordingExpressionEngine());

        registry.TryGetFactory(RoutingComponentTypes.Correlation, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterRoutingComponents_AddsWindowFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(new RecordingExpressionEngine());

        registry.TryGetFactory(RoutingComponentTypes.Window, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterRoutingComponents_AddsJoinFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(new RecordingExpressionEngine());

        registry.TryGetFactory(RoutingComponentTypes.Join, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterRoutingComponents_RequiresExpressionEngine()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(_ => { });
        registry.TryGetFactory(RoutingComponentTypes.Switch, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(RoutingTestHost.CreateContext(
                RoutingComponentTypes.Switch,
                new { expression = "route" })));

        exception.Message.ShouldContain("expression engine");
    }
}
