using FluxFlow.Components.Expressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Expressions.Tests;

public sealed class FlowContextFactoryRegistryTests
{
    [Fact]
    public void Resolve_ReturnsExactFactory()
    {
        var defaultFactory = new TestFactory("default");
        var exactFactory = new TestFactory("exact");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory)
            .Register(typeof(string), exactFactory);

        registry.Resolve(typeof(string)).ShouldBeSameAs(exactFactory);
    }

    [Fact]
    public void Resolve_ReturnsAssignableFactory()
    {
        var defaultFactory = new TestFactory("default");
        var baseFactory = new TestFactory("base");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory)
            .Register(typeof(BaseMessage), baseFactory);

        registry.Resolve(typeof(DerivedMessage)).ShouldBeSameAs(baseFactory);
    }

    [Fact]
    public void Resolve_ReturnsMostSpecificAssignableFactory()
    {
        var defaultFactory = new TestFactory("default");
        var baseFactory = new TestFactory("base");
        var derivedFactory = new TestFactory("derived");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory)
            .Register(typeof(BaseMessage), baseFactory)
            .Register(typeof(DerivedMessage), derivedFactory);

        registry.Resolve(typeof(MoreDerivedMessage)).ShouldBeSameAs(derivedFactory);
    }

    [Fact]
    public void Resolve_ReturnsDefaultFactoryWhenNoSpecificFactoryMatches()
    {
        var defaultFactory = new TestFactory("default");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory);

        registry.Resolve(typeof(int)).ShouldBeSameAs(defaultFactory);
    }

    [Fact]
    public void UseDefault_ReplacesDefaultFactory()
    {
        var first = new TestFactory("first");
        var second = new TestFactory("second");
        var registry = new FlowContextFactoryRegistry<TestFactory>(first)
            .UseDefault(second);

        registry.Resolve(typeof(int)).ShouldBeSameAs(second);
    }

    private sealed record TestFactory(string Name);

    private record BaseMessage;

    private record DerivedMessage : BaseMessage;

    private sealed record MoreDerivedMessage : DerivedMessage;
}
