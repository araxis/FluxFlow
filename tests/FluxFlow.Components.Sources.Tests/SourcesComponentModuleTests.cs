using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class SourcesComponentModuleTests
{
    [Fact]
    public void RegisterSourcesComponents_AddsSourceFactories()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSourcesComponents();

        registry.TryGetFactory(SourcesComponentTypes.Generated, out _).ShouldBeTrue();
        registry.TryGetFactory(SourcesComponentTypes.Sequence, out _).ShouldBeTrue();
    }
}
