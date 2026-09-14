using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public static class FluxFlowRegistrationExtensions
{
    public static IServiceCollection AddComponent(this IServiceCollection services, string type,
        Func<ComponentActivationContext, IFlowNode> factory)
    {
        return RegisterFactory(services, type, factory);
    }

    public static IServiceCollection AddComponent(this IServiceCollection services, string type,
        Func<ComponentActivationContext, ValueTask<IFlowNode>> factory)
        => RegisterFactory(services, type, factory);

    private static IServiceCollection RegisterFactory<TFactory>(IServiceCollection services, string type, TFactory factory)
        where TFactory : Delegate
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        type = type.Trim();
        var existing = DirectFactories(services).FirstOrDefault(p => p.Key == type).Value;
        if (existing is not null)
        {
            if (existing.Equals(factory)) return services;
            throw new InvalidOperationException($"Component type '{type}' has conflicting registrations.");
        }
        services.Add(ServiceDescriptor.Singleton(typeof(KeyValuePair<string, TFactory>),
            new KeyValuePair<string, TFactory>(type, factory)));
        return services;
    }

    private static IEnumerable<KeyValuePair<string, Delegate>> DirectFactories(IServiceCollection services)
        => services.Select(s => s.ImplementationInstance)
            .OfType<KeyValuePair<string, Func<ComponentActivationContext, IFlowNode>>>()
            .Select(p => new KeyValuePair<string, Delegate>(p.Key, p.Value))
            .Concat(services.Select(s => s.ImplementationInstance)
                .OfType<KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>>()
                .Select(p => new KeyValuePair<string, Delegate>(p.Key, p.Value)));

    public static IReadOnlyDictionary<string, ComponentFactory> CreateFactoryMap(IServiceProvider services,
        IEnumerable<KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>>? componentFactories = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var map = new Dictionary<string, ComponentFactory>(StringComparer.Ordinal);
        var factories = services.GetServices<KeyValuePair<string, Func<ComponentActivationContext, IFlowNode>>>()
            .Select(pair => new KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>(
                pair.Key, context => ValueTask.FromResult(pair.Value(context))))
            .Concat(services.GetServices<KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>>())
            .Concat(componentFactories ?? []);
        var registered = new Dictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>(StringComparer.Ordinal);
        foreach (var (type, factory) in factories)
        {
            if (registered.TryGetValue(type, out var previous))
            {
                if (previous == factory) continue;
                throw new InvalidOperationException($"Component type '{type}' has conflicting registrations.");
            }
            registered.Add(type, factory);
            map.Add(type, async context =>
            {
                var node = await factory(context).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Factory for '{type}' returned null.");
                return await context.BindAsync(node).ConfigureAwait(false);
            });
        }
        return map;
    }

}
