using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Expressions.Tests;

public sealed class ExpressionServiceCollectionExtensionsTests
{
    [Fact]
    public void Service_registration_registers_keyed_expression_engine_and_context_factory()
    {
        var engine = new TestExpressionEngine("primary");
        var contextFactory = new TestContextFactory();
        var services = new ServiceCollection()
            .AddFluxFlowExpressionEngine("engine", engine)
            .AddFluxFlowMapContextFactory<InputMessage>("context", contextFactory);

        using var provider = services.BuildServiceProvider();
        var resolvedEngine = provider.GetRequiredKeyedService<IFlowExpressionEngine>("engine");
        var resolvedContextFactory = provider.GetRequiredKeyedService<IFlowMapContextFactory<InputMessage>>("context");
        var context = resolvedContextFactory.Create(new InputMessage("sample"));

        resolvedEngine.ShouldBeSameAs(engine);
        resolvedContextFactory.ShouldBeSameAs(contextFactory);
        context.Variables["input"].ShouldBe(new InputMessage("sample"));
    }

    [Fact]
    public void Service_registration_trims_keyed_names()
    {
        var engine = new TestExpressionEngine("primary");
        var contextFactory = new TestContextFactory();
        var services = new ServiceCollection()
            .AddFluxFlowExpressionEngine(" engine ", engine)
            .AddFluxFlowMapContextFactory<InputMessage>(" context ", contextFactory);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IFlowExpressionEngine>("engine")
            .ShouldBeSameAs(engine);
        provider.GetRequiredKeyedService<IFlowMapContextFactory<InputMessage>>("context")
            .ShouldBeSameAs(contextFactory);
    }

    [Fact]
    public void Service_registration_passes_provider_to_factories()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Dependency("from-provider"));
        services
            .AddFluxFlowExpressionEngine(
                "engine",
                provider => new TestExpressionEngine(provider.GetRequiredService<Dependency>().Value))
            .AddFluxFlowMapContextFactory<InputMessage>(
                "context",
                provider => new TestContextFactory(provider.GetRequiredService<Dependency>().Value));

        using var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredKeyedService<IFlowExpressionEngine>("engine");
        var contextFactory = provider.GetRequiredKeyedService<IFlowMapContextFactory<InputMessage>>("context");
        var context = contextFactory.Create(new InputMessage("sample"));

        engine.Name.ShouldBe("from-provider");
        context.Variables["source"].ShouldBe("from-provider");
    }

    [Fact]
    public void Service_registration_rejects_invalid_arguments()
    {
        var services = new ServiceCollection();
        var engine = new TestExpressionEngine("primary");
        var contextFactory = new TestContextFactory();

        Should.Throw<ArgumentNullException>(() =>
            ExpressionServiceCollectionExtensions.AddFluxFlowExpressionEngine(null!, "engine", engine))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowExpressionEngine(" ", engine))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowExpressionEngine("engine", (IFlowExpressionEngine)null!))
            .ParamName.ShouldBe("engine");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowExpressionEngine("engine", (Func<IServiceProvider, IFlowExpressionEngine>)null!))
            .ParamName.ShouldBe("engineFactory");

        Should.Throw<ArgumentNullException>(() =>
            ExpressionServiceCollectionExtensions.AddFluxFlowMapContextFactory<InputMessage>(null!, "context", contextFactory))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowMapContextFactory<InputMessage>(" ", contextFactory))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowMapContextFactory<InputMessage>("context", (IFlowMapContextFactory<InputMessage>)null!))
            .ParamName.ShouldBe("contextFactory");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowMapContextFactory<InputMessage>(
                "context",
                (Func<IServiceProvider, IFlowMapContextFactory<InputMessage>>)null!))
            .ParamName.ShouldBe("contextFactory");
    }

    [Fact]
    public void Service_registration_rejects_null_factory_results()
    {
        var services = new ServiceCollection()
            .AddFluxFlowExpressionEngine("engine", _ => null!)
            .AddFluxFlowMapContextFactory<InputMessage>("context", _ => null!);

        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IFlowExpressionEngine>("engine"))
            .Message.ShouldContain("Expression engine factory returned null.");
        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IFlowMapContextFactory<InputMessage>>("context"))
            .Message.ShouldContain("Map context factory provider returned null.");
    }

    private sealed record Dependency(string Value);

    private sealed record InputMessage(string Value);

    private sealed class TestExpressionEngine(string name) : IFlowExpressionEngine
    {
        public string Name { get; } = name;

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
            => null;
    }

    private sealed class TestContextFactory(string? source = null) : IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["source"] = source
                }
            };
    }
}
