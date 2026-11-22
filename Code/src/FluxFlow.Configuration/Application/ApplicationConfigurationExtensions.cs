using FluxFlow.Configuration.Components;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Configuration.Application;

/// <summary>Registers deferred factories. Reading configuration does not construct components.</summary>
public static class ApplicationConfigurationExtensions
{
    public static IServiceCollection AddFluxFlowApplication(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var sections = configuration.GetChildren().ToArray();
        if (!sections.Any(section => section.Key.Equals("Workflows", StringComparison.OrdinalIgnoreCase)))
            throw new CompositionConfigurationException("Application configuration requires 'Workflows'.");
        foreach (var section in sections)
            if (!section.Key.Equals("Workflows", StringComparison.OrdinalIgnoreCase)
                && !section.Key.Equals("Resources", StringComparison.OrdinalIgnoreCase))
                throw new CompositionConfigurationException($"Unknown application section '{section.Path}'.");
        var workflows = configuration.GetSection("Workflows");
        RequireObject(workflows);
        // Stage standard DI descriptors so a bad section cannot leave a partial registration.
        IServiceCollection registrations = new ServiceCollection();
        foreach (var descriptor in services) registrations.Add(descriptor);
        registrations.TryAddSingleton<FlowApplication>();
        RegisterResources(registrations, configuration.GetSection("Resources"), null, []);
        foreach (var workflow in workflows.GetChildren())
        {
            RequireObject(workflow);
            var workflowName = workflow.Key;
            registrations.AddFlowWorkflow(workflowName);
            RegisterResources(registrations, workflow.GetSection("Resources"), workflowName, []);
            foreach (var section in workflow.GetChildren())
            {
                if (section.Key.Equals("Resources", StringComparison.OrdinalIgnoreCase)) continue;
                RegisterComponent(registrations, section);
            }
        }
        ((ServiceCollection)registrations).MakeReadOnly();
        services.Clear();
        foreach (var registration in registrations) services.Add(registration);
        return services;
    }

    private static void RegisterComponent(IServiceCollection services, IConfigurationSection section)
    {
        var address = section.GetComponentAddress();
        try
        {
            RequireObject(section);
            var type = RequiredString(section.GetSection("Type"));
            var register = FindRegistration<ComponentRegistration>(services, type)
                ?? throw new InvalidOperationException($"Component type '{type}' has no configuration registration.");
            register(services, section);
            if (!services.Any(item => item.IsKeyedService
                && item.ServiceType == typeof(IFlowNode) && Equals(item.ServiceKey, address.Value)
                && item.Lifetime == ServiceLifetime.Scoped && item.KeyedImplementationFactory is not null))
                throw new InvalidOperationException($"Registration did not provide a factory for '{address}'.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Component '{address}' at '{section.Path}' could not be registered: {error.Message}", error);
        }
    }

    private static void RegisterResources(IServiceCollection services, IConfigurationSection section,
        string? workflow, string[] path)
    {
        RequireObject(section);
        var children = section.GetChildren().ToArray();
        if (path.Length > 0 && children.Any(child => child.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)))
        {
            var address = workflow is null ? ApplicationAddress.Resource(path) : ApplicationAddress.WorkflowResource(workflow, path);
            try
            {
                var type = RequiredString(section.GetSection("Type"));
                var register = FindRegistration<ResourceRegistration>(services, type)
                    ?? throw new InvalidOperationException($"Resource type '{type}' has no configuration registration.");
                register(services, address, section);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Resource '{address}' at '{section.Path}' could not be registered: {error.Message}", error);
            }
            return;
        }
        foreach (var child in children)
        {
            if (path.Length == 0 && child.Key.Equals("Functions", StringComparison.OrdinalIgnoreCase))
                throw new CompositionConfigurationException($"'{child.Path}': functions are not supported.");
            var key = path.Length == 0 && child.Key.Equals("Connections", StringComparison.OrdinalIgnoreCase) ? "Connections" : child.Key;
            RegisterResources(services, child, workflow, [.. path, key]);
        }
    }

    private static T? FindRegistration<T>(IServiceCollection services, string type) where T : Delegate
        => services.LastOrDefault(item => item.IsKeyedService
            && item.ServiceType == typeof(T) && Equals(item.ServiceKey, type))?.KeyedImplementationInstance as T;

    private static string RequiredString(IConfigurationSection section)
        => !string.IsNullOrWhiteSpace(section.Value) && !section.GetChildren().Any()
            ? section.Value : throw new CompositionConfigurationException($"'{section.Path}' requires a non-empty scalar value.");

    private static void RequireObject(IConfigurationSection section)
    {
        if (section.Value is not null)
            throw new CompositionConfigurationException($"'{section.Path}' requires child sections, not a scalar value.");
    }

    /// <summary>Gets workflow.component from a component section, including a host-selected root.</summary>
    public static ComponentAddress GetComponentAddress(this IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var parts = section.Path.Split(ConfigurationPath.KeyDelimiter);
        if (parts.Length < 3 || !parts[^3].Equals("workflows", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{section.Path}' is not a workflow component section.", nameof(section));
        return ComponentAddress.Create(parts[^2], parts[^1]);
    }
}
