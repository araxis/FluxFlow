using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Application;

public static class ResourceRegistrationExtensions
{
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

    public static IServiceCollection AddFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address);
        ArgumentNullException.ThrowIfNull(factory);
        if (address.WorkflowName is null)
            services.AddKeyedSingleton<TService>(address.Value, (provider, _) => factory(provider));
        else
        {
            var workflow = address.WorkflowName;
            if (!services.Any(item => item.IsKeyedService && item.ServiceType == typeof(FlowWorkflow) && Equals(item.ServiceKey, workflow)))
                services.AddFlowWorkflow(workflow);
            services.AddKeyedScoped<TService>(address.Value, (provider, _) =>
            {
                if (!ReferenceEquals(provider, provider.GetWorkflowServices(workflow)))
                    throw new InvalidOperationException($"Resolve resource '{address}' from its workflow's Services. Use an application resource for sharing.");
                return factory(provider);
            });
        }
        return services;
    }

    public static IServiceCollection AddExternalFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        TService service)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address);
        ArgumentNullException.ThrowIfNull(service);
        services.AddKeyedSingleton(address.Value, service);
        return services;
    }

    private static void ValidateAddress(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != ApplicationAddressKind.Resource)
        {
            throw new ArgumentException(
                $"Address '{address}' must be a {ApplicationAddressKind.Resource} address.",
                nameof(address));
        }
    }
}
