using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Assertions.Tests;

public sealed class AssertionsComponentModuleTests
{
    [Fact]
    public void RegisterAssertionsComponents_AddsAssertionFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterAssertionsComponents(new RecordingExpressionEngine());

        registry.TryGetFactory(AssertionsComponentTypes.Assert, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterAssertionsComponents_RequiresExpressionEngine()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterAssertionsComponents(_ => { });
        registry.TryGetFactory(AssertionsComponentTypes.Assert, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(AssertionsTestHost.CreateContext(
                AssertionsComponentTypes.Assert,
                new { expression = "input" })));

        exception.Message.ShouldContain("expression engine");
    }
}
