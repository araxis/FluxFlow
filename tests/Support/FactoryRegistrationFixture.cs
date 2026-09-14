using FluxFlow.Composition;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Testing;

internal static class FactoryRegistrationFixture
{
    public static IServiceCollection AddTestFactory(this IServiceCollection services,
        KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> factory)
        => services.AddComponent(factory.Key, factory.Value);

    public static IServiceCollection AddTestFactories(this IServiceCollection services,
        IEnumerable<KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>> factories)
    {
        foreach (var (type, factory) in factories) services.AddComponent(type, factory);
        return services;
    }
}
