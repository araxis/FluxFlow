using FluxFlow.Components.Expressions;
using FluxFlow.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Expressions.Tests;

public sealed class FlowExpressionEngineRegistryTests
{
    [Fact]
    public void Resolve_ReturnsDefaultEngineWhenNameIsEmpty()
    {
        var engine = new TestExpressionEngine("default");
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .Use(engine);

        registry.Resolve(null).ShouldBeSameAs(engine);
        registry.Resolve("").ShouldBeSameAs(engine);
    }

    [Fact]
    public void Resolve_ReturnsNamedEngineCaseInsensitively()
    {
        var engine = new TestExpressionEngine("primary");
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .Use(engine);

        registry.Resolve("PRIMARY").ShouldBeSameAs(engine);
    }

    [Fact]
    public void Resolve_UsesResolverWhenConfigured()
    {
        var engine = new TestExpressionEngine("custom");
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .UseResolver(name =>
            {
                name.ShouldBe("custom");
                return engine;
            });

        registry.Resolve("custom").ShouldBeSameAs(engine);
    }

    [Fact]
    public void Resolve_RejectsMissingDefaultEngine()
    {
        var registry = new FlowExpressionEngineRegistry("Mapping");

        var exception = Should.Throw<InvalidOperationException>(() => registry.Resolve(null));

        exception.Message.ShouldContain("Mapping components require an expression engine");
    }

    [Fact]
    public void Resolve_RejectsUnknownNamedEngine()
    {
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .Use(new TestExpressionEngine("primary"));

        var exception = Should.Throw<InvalidOperationException>(() => registry.Resolve("other"));

        exception.Message.ShouldContain("Mapping expression engine 'other' is not registered");
    }

    [Fact]
    public void Resolve_RejectsNullResolverResult()
    {
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .UseResolver(_ => null!);

        var exception = Should.Throw<InvalidOperationException>(() => registry.Resolve("missing"));

        exception.Message.ShouldContain("Mapping expression engine resolver returned null");
    }

    private sealed class TestExpressionEngine(string name) : IFlowExpressionEngine
    {
        public string Name { get; } = name;

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => null;
    }
}
