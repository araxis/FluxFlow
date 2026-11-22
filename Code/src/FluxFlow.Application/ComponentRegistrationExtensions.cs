using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Application;

/// <summary>Registers component factories and native ports using the standard service collection.</summary>
public static class ComponentRegistrationExtensions
{
    /// <summary>Registers one node per workflow execution scope under its unique address.</summary>
    public static IServiceCollection AddComponentFactory(this IServiceCollection services, ComponentAddress address, ComponentFactory factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(address);
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(IFlowNode)
            && Equals(item.ServiceKey, address.Value)))
            throw new ArgumentException($"Component '{address}' is already registered.", nameof(address));

        var workflowName = address.WorkflowName;
        if (!services.Any(item => item.IsKeyedService && item.ServiceType == typeof(FlowWorkflow)
            && Equals(item.ServiceKey, workflowName)))
            services.AddFlowWorkflow(workflowName);
        services.AddKeyedSingleton(workflowName, address);
        services.AddKeyedScoped<IFlowNode>(address.Value, (provider, _) =>
        {
            if (!ReferenceEquals(provider, provider.GetWorkflowServices(workflowName)))
                throw new InvalidOperationException($"Resolve component '{address}' from its workflow's Services.");
            var node = factory(provider) ?? throw new InvalidOperationException($"Factory '{address}' returned no component.");
            // Attach diagnostic identity and reject aliases before DI captures ownership.
            FlowDiagnostics.AttachComponentAddress(node, address.Value);
            provider.GetRequiredKeyedService<FlowWorkflow>(workflowName).Execution.Components.Enqueue(new(address, node));
            return node;
        });
        return services;
    }

    /// <summary>Registers a component's native input. The block must not implement IDisposable
    /// or IAsyncDisposable; its component owns cleanup and DI owns the component.</summary>
    public static IServiceCollection AddInputPort<TNode>(this IServiceCollection services,
        ComponentAddress address, string name, Func<TNode, ITargetBlock<FlowMessage>> select) where TNode : IFlowNode
        => AddPort(services, address, name, select);

    /// <summary>Registers a component's native output. The block must not implement IDisposable
    /// or IAsyncDisposable; its component owns cleanup and DI owns the component.</summary>
    public static IServiceCollection AddOutputPort<TNode>(this IServiceCollection services,
        ComponentAddress address, string name, Func<TNode, ISourceBlock<FlowMessage>> select) where TNode : IFlowNode
        => AddPort(services, address, name, select);

    private static IServiceCollection AddPort<TNode, TPort>(IServiceCollection services,
        ComponentAddress address, string name, Func<TNode, TPort> select)
        where TNode : IFlowNode where TPort : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(select);
        var portAddress = RequireUniquePort<TPort>(services, address, name);
        services.AddKeyedScoped<TPort>(portAddress.Value,
            (provider, _) => RequireNativePort(select((TNode)provider.GetRequiredKeyedService<IFlowNode>(address.Value)), portAddress.Value));
        services.AddKeyedSingleton<PortAddress>(typeof(TPort), portAddress);
        return services;
    }

    public static IServiceCollection AddEventPort<TNode>(this IServiceCollection services,
        ComponentAddress address, Func<TNode, ISourceBlock<FlowMessage>> select) where TNode : IFlowNode
    {
        return services.AddEventPort(address, "Events", select);
    }

    /// <summary>Registers a named, routable event output with application address headers.</summary>
    public static IServiceCollection AddEventPort<TNode>(this IServiceCollection services,
        ComponentAddress address, string name, Func<TNode, ISourceBlock<FlowMessage>> select) where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(select);
        var portAddress = RequireUniquePort<ISourceBlock<FlowMessage>>(services, address, name);
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(ComponentEventBridge)
            && Equals(item.ServiceKey, portAddress.Value)))
            throw new ArgumentException($"Events '{portAddress}' are already registered.", nameof(name));
        services.AddKeyedScoped<ComponentEventBridge>(portAddress.Value, (provider, _) =>
        {
            var node = (TNode)provider.GetRequiredKeyedService<IFlowNode>(address.Value);
            return new ComponentEventBridge(address,
                select(node) ?? throw new InvalidOperationException($"Events '{portAddress}' selector returned no block."),
                node.Completion);
        });
        services.AddKeyedSingleton((address.Value, typeof(ComponentEventBridge)), portAddress.Value);
        services.AddKeyedScoped<ISourceBlock<FlowMessage>>(portAddress.Value,
            (provider, _) => provider.GetRequiredKeyedService<ComponentEventBridge>(portAddress.Value).Output);
        services.AddKeyedSingleton<PortAddress>(typeof(ISourceBlock<FlowMessage>), portAddress);
        return services;
    }

    // DI captures disposable factory results, even when another service already owns
    // that same object. Ports expose native blocks; their component owns cleanup.
    private static TPort RequireNativePort<TPort>(TPort? port, string address) where TPort : class
    {
        if (port is null) throw new InvalidOperationException($"Port '{address}' selector returned no block.");
        if (port is IDisposable or IAsyncDisposable)
            throw new InvalidOperationException($"Port '{address}' must expose a separate, non-disposable Dataflow block. Its component owns disposal.");
        return port;
    }

    private static PortAddress RequireUniquePort<TPort>(IServiceCollection services, ComponentAddress address, string name)
    {
        ArgumentNullException.ThrowIfNull(address);
        var portAddress = address.Port(name);
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(TPort)
            && Equals(item.ServiceKey, portAddress.Value)))
            throw new ArgumentException($"Port '{portAddress}' is already registered.", nameof(name));
        return portAddress;
    }

    // Enumerate immutable registration data, without a second native-block lookup table.
    internal static IEnumerable<PortAddress> GetPortRegistrations<TPort>(this IServiceProvider services)
        => services.GetKeyedServices<PortAddress>(typeof(TPort));
}
