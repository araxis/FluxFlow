using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Control.Tests;

public sealed class ControlComponentModuleTests
{
    [Fact]
    public void RegisterControlComponents_AddsControlFactories()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterControlComponents(new RecordingExpressionEngine());

        registry.TryGetFactory(ControlComponentTypes.Filter, out _).ShouldBeTrue();
        registry.TryGetFactory(ControlComponentTypes.When, out _).ShouldBeTrue();
        registry.TryGetFactory(ControlComponentTypes.Assert, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterControlComponents_RequiresExpressionEngine()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterControlComponents(_ => { });
        registry.TryGetFactory(ControlComponentTypes.Filter, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(ControlTestHost.CreateContext(
                ControlComponentTypes.Filter,
                new { expression = "input" })));

        exception.Message.ShouldContain("expression engine");
    }
}
