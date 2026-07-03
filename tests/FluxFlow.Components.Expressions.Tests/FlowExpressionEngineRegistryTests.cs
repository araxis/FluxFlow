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
    public void Resolve_DoesNotUseFirstEngineAsDefaultWhenDisabled()
    {
        var engine = new TestExpressionEngine("primary");
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .Use(engine, useAsDefault: false);

        registry.Resolve("primary").ShouldBeSameAs(engine);
        var exception = Should.Throw<InvalidOperationException>(() => registry.Resolve(null));
        exception.Message.ShouldContain("Mapping components require an expression engine");
    }

    [Fact]
    public void Resolve_KeepsExistingDefaultWhenRegisteringNamedOnlyEngine()
    {
        var defaultEngine = new TestExpressionEngine("default");
        var namedEngine = new TestExpressionEngine("named");
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .Use(defaultEngine)
            .Use(namedEngine, useAsDefault: false);

        registry.Resolve(null).ShouldBeSameAs(defaultEngine);
        registry.Resolve("named").ShouldBeSameAs(namedEngine);
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
    public void Resolve_ProvidesNormalizedNameToResolver()
    {
        var engine = new TestExpressionEngine("custom");
        string? capturedName = "unset";
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .UseResolver(name =>
            {
                capturedName = name;
                return engine;
            });

        registry.Resolve(" custom ").ShouldBeSameAs(engine);
        capturedName.ShouldBe("custom");

        registry.Resolve(" ").ShouldBeSameAs(engine);
        capturedName.ShouldBeNull();
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
    public void Resolve_UsesNormalizedNameInUnknownEngineError()
    {
        var registry = new FlowExpressionEngineRegistry("Mapping")
            .Use(new TestExpressionEngine("primary"));

        var exception = Should.Throw<InvalidOperationException>(() => registry.Resolve(" other "));

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

    [Fact]
    public void Public_methods_reject_invalid_arguments()
    {
        var registry = new FlowExpressionEngineRegistry("Mapping");

        Should.Throw<ArgumentException>(() => new FlowExpressionEngineRegistry(" "))
            .ParamName.ShouldBe("scopeName");
        Should.Throw<ArgumentNullException>(() => registry.Use(null!))
            .ParamName.ShouldBe("expressionEngine");

        var missingNameException = Should.Throw<ArgumentException>(() =>
            registry.Use(new TestExpressionEngine(" ")));
        missingNameException.ParamName.ShouldBe("expressionEngine");
        missingNameException.Message.ShouldContain("Expression engine name is required.");

        Should.Throw<ArgumentNullException>(() => registry.UseResolver(null!))
            .ParamName.ShouldBe("resolver");
    }

    private sealed class TestExpressionEngine(string name) : IFlowExpressionEngine
    {
        public string Name { get; } = name;

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => null;
    }
}
