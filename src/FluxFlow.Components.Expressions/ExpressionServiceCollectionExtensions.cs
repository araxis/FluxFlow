using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Expressions;

public static class ExpressionServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowExpressionEngine(
        this IServiceCollection services,
        string name,
        IFlowExpressionEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return services.AddFluxFlowExpressionEngine(name, _ => engine);
    }

    public static IServiceCollection AddFluxFlowExpressionEngine(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IFlowExpressionEngine> engineFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(engineFactory);

        var normalizedName = name.Trim();
        services.AddKeyedSingleton<IFlowExpressionEngine>(
            normalizedName,
            (provider, _) => engineFactory(provider)
                ?? throw new InvalidOperationException("Expression engine factory returned null."));

        return services;
    }

    public static IServiceCollection AddFluxFlowMapContextFactory<TInput>(
        this IServiceCollection services,
        string name,
        IFlowMapContextFactory<TInput> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        return services.AddFluxFlowMapContextFactory<TInput>(name, _ => contextFactory);
    }

    public static IServiceCollection AddFluxFlowMapContextFactory<TInput>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IFlowMapContextFactory<TInput>> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(contextFactory);

        var normalizedName = name.Trim();
        services.AddKeyedSingleton<IFlowMapContextFactory<TInput>>(
            normalizedName,
            (provider, _) => contextFactory(provider)
                ?? throw new InvalidOperationException("Map context factory provider returned null."));

        return services;
    }
}
