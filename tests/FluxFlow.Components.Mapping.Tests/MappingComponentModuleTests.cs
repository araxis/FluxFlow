using FluxFlow.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mapping.Tests;

public sealed class MappingComponentModuleTests
{
    [Fact]
    public void RegisterMappingComponents_AddsMapperFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMappingComponents(new TestExpressionEngine());

        registry.TryGetFactory(MappingComponentTypes.Mapper, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterMappingComponents_RequiresExpressionEngine()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMappingComponents(_ => { });
        registry.TryGetFactory(MappingComponentTypes.Mapper, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MappingTestHost.CreateContext(new { expression = "input" })));

        exception.Message.ShouldContain("expression engine");
    }

    private sealed class TestExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "test";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => context.Variables["input"];
    }
}
