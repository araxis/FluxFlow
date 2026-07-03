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
    public void Register_GenericInputTypeRegistersFactory()
    {
        var defaultFactory = new TestFactory("default");
        var exactFactory = new TestFactory("exact");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory)
            .Register<string>(exactFactory);

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

    [Fact]
    public void Resolve_ThrowsForIncomparableAssignableCandidates()
    {
        var defaultFactory = new TestFactory("default");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory)
            .Register(typeof(IFirstMarker), new TestFactory("first"))
            .Register(typeof(ISecondMarker), new TestFactory("second"));

        var exception = Should.Throw<InvalidOperationException>(
            () => registry.Resolve(typeof(BothMarkersMessage)));

        exception.Message.ShouldContain(nameof(IFirstMarker));
        exception.Message.ShouldContain(nameof(ISecondMarker));
        exception.Message.ShouldContain("no single registration is more specific");
    }

    [Fact]
    public void Resolve_PrefersCandidateAssignableToAllOtherCandidates()
    {
        var defaultFactory = new TestFactory("default");
        var exactBase = new TestFactory("both-markers");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory)
            .Register(typeof(IFirstMarker), new TestFactory("first"))
            .Register(typeof(ISecondMarker), new TestFactory("second"))
            .Register(typeof(BothMarkersMessage), exactBase);

        registry.Resolve(typeof(DerivedBothMarkersMessage)).ShouldBeSameAs(exactBase);
    }

    [Fact]
    public void Public_methods_reject_null_arguments()
    {
        var defaultFactory = new TestFactory("default");
        var registry = new FlowContextFactoryRegistry<TestFactory>(defaultFactory);

        Should.Throw<ArgumentNullException>(() => new FlowContextFactoryRegistry<TestFactory>(null!))
            .ParamName.ShouldBe("defaultFactory");
        Should.Throw<ArgumentNullException>(() => registry.UseDefault(null!))
            .ParamName.ShouldBe("factory");
        Should.Throw<ArgumentNullException>(() => registry.Register(null!, defaultFactory))
            .ParamName.ShouldBe("inputType");
        Should.Throw<ArgumentNullException>(() => registry.Register(typeof(string), null!))
            .ParamName.ShouldBe("factory");
        Should.Throw<ArgumentNullException>(() => registry.Resolve(null!))
            .ParamName.ShouldBe("inputType");
    }

    private sealed record TestFactory(string Name);

    private record BaseMessage;

    private record DerivedMessage : BaseMessage;

    private sealed record MoreDerivedMessage : DerivedMessage;

    private interface IFirstMarker;

    private interface ISecondMarker;

    private record BothMarkersMessage : IFirstMarker, ISecondMarker;

    private sealed record DerivedBothMarkersMessage : BothMarkersMessage;
}
