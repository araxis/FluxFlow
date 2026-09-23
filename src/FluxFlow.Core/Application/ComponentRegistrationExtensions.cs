using FluxFlow.Core.Addressing;
using FluxFlow.Core.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Application;

/// <summary>Registers and composes components using the standard service collection.</summary>
public static class ComponentRegistrationExtensions
{
    private static readonly ConditionalWeakTable<IFlowNode, object> RegisteredComponents = new();
    /// <summary>
    /// Constructs workflows, components and link declarations without starting sources.
    /// The caller stops/disposes the application before disposing its host provider.
    /// DI owns constructed components and resources, including after failed composition.
    /// </summary>
    public static Task<FlowApplication> BuildApplicationAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();
        var application = services.GetRequiredService<FlowApplication>();
        return services.BuildApplicationAsync(application.LinkCapacity, cancellationToken);
    }

    /// <summary>Composes the registered application with an explicit link capacity.
    /// Later calls must use the same capacity.</summary>
    public static async Task<FlowApplication> BuildApplicationAsync(this IServiceProvider services,
        int linkCapacity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();
        var application = services.GetRequiredService<FlowApplication>();
        await application.ComposeAsync(services, linkCapacity, cancellationToken).ConfigureAwait(false);
        return application;
    }

    /// <summary>Registers resource factories transactionally in the host service collection.
    /// The host provider constructs and owns them; registration creates no provider or resource.</summary>
    public static IServiceCollection AddApplicationResources(this IServiceCollection services, Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(register);
        var resources = new ServiceCollection();
        foreach (var descriptor in services) ((IServiceCollection)resources).Add(descriptor);
        register(resources);
        resources.MakeReadOnly();
        services.Clear();
        foreach (var descriptor in resources) services.Add(descriptor);
        return services;
    }

    /// <summary>Registers a workflow name, including workflows without components.</summary>
    public static IServiceCollection AddFlowWorkflow(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ApplicationAddress.RequireWorkflowName(name, nameof(name));
        var workflows = ReadWorkflows(services);
        if (workflows.ContainsKey(name))
            throw new ArgumentException($"Workflow '{name}' is already registered.", nameof(name));
        workflows.Add(name, []);
        services.TryAddSingleton<FlowApplication>();
        StoreWorkflows(services, workflows);
        return services;
    }

    /// <summary>Registers a factory under its unique workflow.component address, without invoking it.</summary>
    public static IServiceCollection AddComponentFactory(this IServiceCollection services, string address, ComponentFactory factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        var parsed = ApplicationAddress.Parse(address);
        if (parsed.Kind != ApplicationAddressKind.WorkflowComponent)
            throw new ArgumentException("Use a workflow.component address.", nameof(address));
        if (services.Any(item => item.IsKeyedService && (item.ServiceType == typeof(ComponentFactory) || item.ServiceType == typeof(IFlowNode))
            && Equals(item.ServiceKey, address)))
            throw new ArgumentException($"Component '{address}' is already registered.", nameof(address));

        var workflows = ReadWorkflows(services);
        var workflowName = parsed.WorkflowName!;
        var names = workflows.GetValueOrDefault(workflowName) ?? [];
        workflows[workflowName] = [.. names, parsed.ComponentName!];
        services.TryAddSingleton<FlowApplication>();
        StoreWorkflows(services, workflows);
        services.AddKeyedSingleton(address, factory);
        services.AddKeyedSingleton<IFlowNode>(address, (provider, _) =>
        {
            var node = factory(provider) ?? throw new InvalidOperationException($"Factory '{address}' returned no component.");
            // Reject an alias before DI captures its disposal. Native DI still supplies
            // singleton identity/caching; this weak set is only a registration guard.
            lock (RegisteredComponents)
            {
                if (RegisteredComponents.TryGetValue(node, out _))
                    throw new ArgumentException("The same component instance cannot be registered twice.", nameof(node));
                RegisteredComponents.Add(node, new object());
            }
            return node;
        });
        return services;
    }

    /// <summary>Registers a component's native input. The block must not implement IDisposable
    /// or IAsyncDisposable; its component owns cleanup and DI owns the component.</summary>
    public static IServiceCollection AddInputPort<TNode>(this IServiceCollection services,
        string address, string name, Func<TNode, ITargetBlock<FlowMessage>> select,
        bool signal = false) where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(select);
        ApplicationAddress.RequireConstructedSegment(name, nameof(name), "Port name");
        var portAddress = RequireUniquePort<ITargetBlock<FlowMessage>>(services, address, name).Value;
        services.AddKeyedSingleton(typeof(bool), portAddress, signal);
        services.AddKeyedSingleton<ITargetBlock<FlowMessage>>(portAddress,
            (provider, _) => RequireNativePort(select((TNode)provider.GetRequiredKeyedService<IFlowNode>(address)), portAddress));
        AddAddress(services, typeof(ITargetBlock<FlowMessage>), portAddress);
        return services;
    }

    /// <summary>Registers a component's native output. The block must not implement IDisposable
    /// or IAsyncDisposable; its component owns cleanup and DI owns the component.</summary>
    public static IServiceCollection AddOutputPort<TNode>(this IServiceCollection services,
        string address, string name, Func<TNode, ISourceBlock<FlowMessage>> select) where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(select);
        ApplicationAddress.RequireConstructedSegment(name, nameof(name), "Port name");
        var portAddress = RequireUniquePort<ISourceBlock<FlowMessage>>(services, address, name).Value;
        services.AddKeyedSingleton<ISourceBlock<FlowMessage>>(portAddress,
            (provider, _) => RequireNativePort(select((TNode)provider.GetRequiredKeyedService<IFlowNode>(address)), portAddress));
        AddOutputRouting(services, portAddress);
        return services;
    }

    public static IServiceCollection AddEventPort<TNode>(this IServiceCollection services,
        string address, Func<TNode, ISourceBlock<FlowMessage>> select) where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(select);
        AddEventBridge(services, address, address, select);
        return services;
    }

    /// <summary>Registers a named, routable event output with application address headers.</summary>
    public static IServiceCollection AddEventPort<TNode>(this IServiceCollection services,
        string address, string name, Func<TNode, ISourceBlock<FlowMessage>> select) where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(select);
        ApplicationAddress.RequireConstructedSegment(name, nameof(name), "Port name");
        var portAddress = RequireUniquePort<ISourceBlock<FlowMessage>>(services, address, name).Value;
        AddEventBridge(services, address, portAddress, select);
        services.AddKeyedSingleton<ISourceBlock<FlowMessage>>(portAddress,
            (provider, _) => provider.GetRequiredKeyedService<ComponentActivityBridge>(portAddress).Output);
        AddOutputRouting(services, portAddress);
        return services;
    }

    private static void AddEventBridge<TNode>(IServiceCollection services, string address, string eventAddress,
        Func<TNode, ISourceBlock<FlowMessage>> select) where TNode : IFlowNode
    {
        var component = ApplicationAddress.Parse(address);
        if (component.Kind != ApplicationAddressKind.WorkflowComponent)
            throw new ArgumentException("Use a workflow.component address.", nameof(address));
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(ComponentActivityBridge)
            && Equals(item.ServiceKey, eventAddress)))
            throw new ArgumentException($"Events '{eventAddress}' are already registered.", nameof(eventAddress));
        services.AddKeyedSingleton<ComponentActivityBridge>(eventAddress, (provider, _) =>
        {
            var node = (TNode)provider.GetRequiredKeyedService<IFlowNode>(address);
            return new ComponentActivityBridge(component.WorkflowName!, component.ComponentName!,
                select(node) ?? throw new InvalidOperationException($"Events '{eventAddress}' selector returned no block."),
                node.Completion);
        });
        AddAddress(services, (address, typeof(ComponentActivityBridge)), eventAddress);
    }

    private static void AddOutputRouting(IServiceCollection services, string address)
    {
        AddAddress(services, typeof(ISourceBlock<FlowMessage>), address);
        services.AddKeyedSingleton<Action<FlowLink>>(address, (provider, _) =>
            link => provider.GetRequiredService<FlowApplication>().AddLink(address, link));
    }

    private static void AddAddress(IServiceCollection services, object key, string address)
    {
        var previous = services.LastOrDefault(item => item.IsKeyedService
            && item.ServiceType == typeof(IReadOnlyCollection<string>) && Equals(item.ServiceKey, key));
        var addresses = previous?.KeyedImplementationInstance as IReadOnlyCollection<string> ?? [];
        if (previous is not null) services.Remove(previous);
        services.AddKeyedSingleton<IReadOnlyCollection<string>>(key, Array.AsReadOnly<string>([.. addresses, address]));
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

    private static ApplicationAddress RequireUniquePort<TPort>(IServiceCollection services, string address, string name)
    {
        var portAddress = ApplicationAddress.Parse(address).Port(name);
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(TPort)
            && Equals(item.ServiceKey, portAddress.Value)))
            throw new ArgumentException($"Port '{portAddress}' is already registered.", nameof(name));
        return portAddress;
    }

    // Enumerate immutable registration data, without a second native-block lookup table.
    internal static IEnumerable<(string Address, bool IsSignal)> GetPortRegistrations<TPort>(
        this IServiceProvider services)
    {
        foreach (var address in services.GetKeyedService<IReadOnlyCollection<string>>(typeof(TPort)) ?? [])
            yield return (address, typeof(TPort) == typeof(ITargetBlock<FlowMessage>) &&
                services.GetKeyedService<bool>(address));
    }

    // Native DI returns keyed values, not their keys. Preserve only workflow/component
    // names as ordinary data in DI; there is no separate catalog or runtime owner.
    private static Dictionary<string, string[]> ReadWorkflows(IServiceCollection services)
        => new(services.LastOrDefault(item => item.IsKeyedService
            && item.ServiceType == typeof(IReadOnlyDictionary<string, string[]>)
            && Equals(item.ServiceKey, typeof(FlowWorkflow)))?.KeyedImplementationInstance
            as IReadOnlyDictionary<string, string[]> ?? new Dictionary<string, string[]>(), StringComparer.Ordinal);

    private static void StoreWorkflows(IServiceCollection services, Dictionary<string, string[]> workflows)
    {
        for (var index = services.Count - 1; index >= 0; index--)
            if (services[index].IsKeyedService
                && services[index].ServiceType == typeof(IReadOnlyDictionary<string, string[]>)
                && Equals(services[index].ServiceKey, typeof(FlowWorkflow)))
                services.RemoveAt(index);
        services.AddKeyedSingleton<IReadOnlyDictionary<string, string[]>>(
            typeof(FlowWorkflow), new ReadOnlyDictionary<string, string[]>(workflows));
    }
}
