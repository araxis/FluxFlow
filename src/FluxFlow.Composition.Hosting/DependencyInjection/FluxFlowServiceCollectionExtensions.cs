using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Current = FluxFlow.Composition.DependencyInjection.FluxFlowServiceCollectionExtensions;

namespace FluxFlow.Composition.Hosting.DependencyInjection;

[Obsolete("Use FluxFlow.Composition.DependencyInjection.FluxFlowServiceCollectionExtensions.")]
public static class FluxFlowServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, TService> factory)
        where TService : class
        => Current.AddFluxFlowResource(services, address, factory);

    public static IServiceCollection AddExternalFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        TService service)
        where TService : class
        => Current.AddExternalFluxFlowResource(services, address, service);

    public static IServiceCollection AddFluxFlowComponent<TComponent>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, TComponent> factory)
        where TComponent : class, IDataflowBlock
        => Current.AddFluxFlowComponent(services, address, factory);

    public static IServiceCollection AddExternalFluxFlowComponent<TComponent>(
        this IServiceCollection services,
        ApplicationAddress address,
        TComponent component)
        where TComponent : class, IDataflowBlock
        => Current.AddExternalFluxFlowComponent(services, address, component);

    public static IServiceCollection AddFluxFlowInputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ITargetBlock<FlowMessage<T>>> factory)
        => Current.AddFluxFlowInputPort(services, address, factory);

    public static IServiceCollection AddExternalFluxFlowInputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        ITargetBlock<FlowMessage<T>> target)
        => Current.AddExternalFluxFlowInputPort(services, address, target);

    public static IServiceCollection AddFluxFlowInputPortView<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ITargetBlock<FlowMessage<T>>> factory)
        => Current.AddFluxFlowInputPortView(services, address, factory);

    public static IServiceCollection AddFluxFlowOutputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ISourceBlock<FlowMessage<T>>> factory)
        => Current.AddFluxFlowOutputPort(services, address, factory);

    public static IServiceCollection AddExternalFluxFlowOutputPort<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        ISourceBlock<FlowMessage<T>> source)
        => Current.AddExternalFluxFlowOutputPort(services, address, source);

    public static IServiceCollection AddFluxFlowOutputPortView<T>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, ISourceBlock<FlowMessage<T>>> factory)
        => Current.AddFluxFlowOutputPortView(services, address, factory);

    public static IServiceCollection AddFluxFlowSignalTarget(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, IFlowSignalTarget> factory)
        => Current.AddFluxFlowSignalTarget(services, address, factory);

    public static IServiceCollection AddExternalFluxFlowSignalTarget(
        this IServiceCollection services,
        ApplicationAddress address,
        IFlowSignalTarget target)
        => Current.AddExternalFluxFlowSignalTarget(services, address, target);

    public static IServiceCollection AddFluxFlowSignalTargetView(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, IFlowSignalTarget> factory)
        => Current.AddFluxFlowSignalTargetView(services, address, factory);
}
