using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition.DependencyInjection;

public static class FluxFlowServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.Resource);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<TService>(
            address.Value,
            (provider, _) => factory(provider));
        return services;
    }

    public static IServiceCollection AddExternalFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        TService service)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.Resource);
        ArgumentNullException.ThrowIfNull(service);
        services.AddKeyedSingleton(address.Value, service);
        return services;
    }

    public static IServiceCollection AddFluxFlowComponent<TComponent>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, TComponent> factory)
        where TComponent : class, IDataflowBlock
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.WorkflowComponent);
        ArgumentNullException.ThrowIfNull(factory);
        var key = address.Value;
        services.AddKeyedSingleton<TComponent>(key, (provider, _) => factory(provider));
        services.AddKeyedSingleton<IDataflowBlock>(
            key,
            (provider, _) => new DataflowBlockView(
                provider.GetRequiredKeyedService<TComponent>(key)));
        return services;
    }

    public static IServiceCollection AddExternalFluxFlowComponent<TComponent>(
        this IServiceCollection services,
        ApplicationAddress address,
        TComponent component)
        where TComponent : class, IDataflowBlock
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.WorkflowComponent);
        ArgumentNullException.ThrowIfNull(component);
        var key = address.Value;
        services.AddKeyedSingleton(key, component);
        services.AddKeyedSingleton<IDataflowBlock>(
            key,
            (_, _) => new DataflowBlockView(component));
        return services;
    }

    public static IServiceCollection AddFluxFlowInputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ITargetBlock<FlowMessage<T>>> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.WorkflowPort);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<ITargetBlock<FlowMessage<T>>>(
            address.Value,
            (provider, _) => factory(provider));
        return services;
    }

    public static IServiceCollection AddExternalFluxFlowInputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        ITargetBlock<FlowMessage<T>> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return services.AddFluxFlowInputPortView(address, _ => target);
    }

    public static IServiceCollection AddFluxFlowInputPortView<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ITargetBlock<FlowMessage<T>>> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.WorkflowPort);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<ITargetBlock<FlowMessage<T>>>(
            address.Value,
            (provider, _) => new TargetBlockView<FlowMessage<T>>(factory(provider)));
        return services;
    }

    public static IServiceCollection AddFluxFlowOutputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ISourceBlock<FlowMessage<T>>> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateOutputAddress(address);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<ISourceBlock<FlowMessage<T>>>(
            address.Value,
            (provider, _) => factory(provider));
        return services;
    }

    public static IServiceCollection AddExternalFluxFlowOutputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        ISourceBlock<FlowMessage<T>> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return services.AddFluxFlowOutputPortView(address, _ => source);
    }

    public static IServiceCollection AddFluxFlowOutputPortView<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ISourceBlock<FlowMessage<T>>> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateOutputAddress(address);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<ISourceBlock<FlowMessage<T>>>(
            address.Value,
            (provider, _) => new SourceBlockView<FlowMessage<T>>(factory(provider)));
        return services;
    }

    public static IServiceCollection AddFluxFlowSignalTarget(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, IFlowSignalTarget> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.WorkflowPort);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<IFlowSignalTarget>(
            address.Value,
            (provider, _) => factory(provider));
        return services;
    }

    public static IServiceCollection AddExternalFluxFlowSignalTarget(
        this IServiceCollection services,
        ApplicationAddress address,
        IFlowSignalTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return services.AddFluxFlowSignalTargetView(address, _ => target);
    }

    public static IServiceCollection AddFluxFlowSignalTargetView(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, IFlowSignalTarget> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address, ApplicationAddressKind.WorkflowPort);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<IFlowSignalTarget>(
            address.Value,
            (provider, _) => new FlowSignalTargetView(factory(provider)));
        return services;
    }

    private static void ValidateOutputAddress(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind is not (
            ApplicationAddressKind.WorkflowPort or
            ApplicationAddressKind.SystemPort))
        {
            throw new ArgumentException(
                $"Address '{address}' must be a workflow or system output port address.",
                nameof(address));
        }
    }

    private static void ValidateAddress(
        ApplicationAddress address,
        ApplicationAddressKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Address '{address}' must be a {expectedKind} address.",
                nameof(address));
        }
    }
}
