using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.ComponentPackageTemplate.Tests;

public sealed class TemplateComponentModuleTests
{
    [Fact]
    public void RegisterTemplateComponents_AddsEnrichFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTemplateComponents();

        registry.TryGetFactory(TemplateComponentTypes.Enrich, out var factory).ShouldBeTrue();
        factory.ShouldNotBeNull();
    }
}
