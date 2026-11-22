using FluxFlow.Application.Addressing;
using FluxFlow.Application.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Application;

/// <summary>Workflow registration, discovery, application building and links over native DI services.</summary>
public static class FlowApplicationExtensions
{
    /// <summary>Gets the owning workflow's current execution scope. Do not retain it across restart.</summary>
    public static IServiceProvider GetWorkflowServices(this IServiceProvider services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredKeyedService<FlowWorkflow>(name).Services;
    }

    internal static IServiceProvider PortServices(this IServiceProvider services, PortAddress address)
        => address.ComponentAddress is { } component
            ? services.GetKeyedService<FlowWorkflow>(component.WorkflowName)?.Services ?? services : services;

    /// <summary>Enumerates registered component addresses across workflows without constructing
    /// components or resources. Available before application composition; does not indicate runtime state.</summary>
    public static IEnumerable<ComponentAddress> GetComponentAddresses(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetServices<FlowWorkflow>()
            .SelectMany(workflow => services.GetKeyedServices<ComponentAddress>(workflow.Name));
    }

    /// <summary>Enumerates declared input port addresses, including unlinked ports, without
    /// constructing components or blocks. Unrelated host block registrations are excluded.</summary>
    public static IEnumerable<PortAddress> GetInputPortAddresses(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetPortRegistrations<ITargetBlock<FlowMessage>>();
    }

    /// <summary>Enumerates declared output port addresses, including event and unlinked outputs,
    /// without constructing components or blocks. Unrelated host block registrations are excluded.</summary>
    public static IEnumerable<PortAddress> GetOutputPortAddresses(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetPortRegistrations<ISourceBlock<FlowMessage>>();
    }

    /// <summary>Attaches a bounded, non-consuming probe to a declared output, including while running.
    /// Dispose the probe to detach it. Resolving the output may construct its component.</summary>
    public static FlowOutputProbe ObserveOutput(this IServiceProvider services, PortAddress address, int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (!services.GetOutputPortAddresses().Contains(address))
            throw new KeyNotFoundException($"Output '{address}' was not registered.");
        return new FlowOutputProbe(services.PortServices(address).GetRequiredKeyedService<ISourceBlock<FlowMessage>>(address.Value), capacity, address);
    }

    /// <summary>Registers a workflow name, including workflows without components.</summary>
    public static IServiceCollection AddFlowWorkflow(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ApplicationAddress.RequireWorkflowName(name, nameof(name));
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(FlowWorkflow)
            && Equals(item.ServiceKey, name)))
            throw new ArgumentException($"Workflow '{name}' is already registered.", nameof(name));
        services.TryAddSingleton<FlowApplication>();
        services.AddKeyedSingleton<FlowWorkflow>(name, (provider, _) => new FlowWorkflow(name, provider));
        services.AddSingleton(provider => provider.GetRequiredKeyedService<FlowWorkflow>(name));
        return services;
    }

    /// <summary>
    /// Constructs workflows, components and link declarations without starting sources.
    /// The caller stops/disposes the application before disposing its host provider.
    /// DI owns constructed components and resources, including after failed composition.
    /// </summary>
    public static async Task<FlowApplication> BuildApplicationAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();
        var application = services.GetRequiredService<FlowApplication>();
        await application.ComposeAsync(services, cancellationToken).ConfigureAwait(false);
        return application;
    }

    /// <summary>Declares a link between existing full port addresses during Build.
    /// Start connects the route, using native delivery semantics and coordinated completion.</summary>
    public static FlowLink Link(this IServiceProvider ports, PortAddress source, PortAddress target,
        Predicate<FlowMessage>? when = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var output = GetRequiredService<ISourceBlock<FlowMessage>>(ports.PortServices(source), source.Value, "Output");
        var input = GetRequiredService<ITargetBlock<FlowMessage>>(ports.PortServices(target), target.Value, "Input");
        var link = new FlowLink(source, output, target, input, when);
        ports.GetRequiredService<FlowApplication>().AddLink(link);
        return link;
    }

    private static T GetRequiredService<T>(IServiceProvider ports, string address, string role) where T : class
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(address);
        return ports.GetKeyedService<T>(address)
            ?? throw new KeyNotFoundException($"{role} '{address}' was not found.");
    }

    /// <summary>Finds the actual component instance by its workflow.component address.</summary>
    public static IFlowNode GetComponent(this FlowApplication application, ComponentAddress address)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(address);
        return GetRequiredService<IFlowNode>((application.Services ?? throw new InvalidOperationException("Application has not been composed.")).GetWorkflowServices(address.WorkflowName), address.Value, "Component");
    }

}
