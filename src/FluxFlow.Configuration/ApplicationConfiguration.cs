using FluxFlow.Core.Application;
using FluxFlow.Core.Addressing;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluxFlow.Configuration.Links;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Configuration;

/// <summary>Registers deferred factories. Reading configuration does not construct components.</summary>
public static class ApplicationConfiguration
{
    public static ValueTask<IServiceCollection> AddFluxFlowApplicationAsync(this IServiceCollection services,
        IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        if (!configuration.GetChildren().Any(section => section.Key.Equals("Workflows", StringComparison.OrdinalIgnoreCase)))
            throw new CompositionConfigurationException("Application configuration requires 'Workflows'.");
        foreach (var section in configuration.GetChildren())
            if (!section.Key.Equals("Workflows", StringComparison.OrdinalIgnoreCase)
                && !section.Key.Equals("Resources", StringComparison.OrdinalIgnoreCase))
                throw new CompositionConfigurationException($"Unknown application section '{section.Path}'.");
        var workflows = configuration.GetSection("Workflows");
        RequireObject(workflows);
        // Stage standard DI descriptors so a bad section cannot leave a partial registration.
        IServiceCollection registrations = new ServiceCollection();
        foreach (var descriptor in services) registrations.Add(descriptor);
        registrations.TryAddSingleton<FlowApplication>();
        // One compilation session per provider. Registration never resolves engines or compiles expressions.
        var conditionSession = new object();
        registrations.AddKeyedSingleton<ApplicationConditionCompiler>(conditionSession, (provider, _) =>
            new ApplicationConditionCompiler(provider.GetService<IFlowExpressionEngine>(),
                name => provider.GetKeyedService<IFlowExpressionEngine>(name)));
        RegisterResources(registrations, configuration.GetSection("Resources"), null, [], cancellationToken);
        foreach (var workflow in workflows.GetChildren())
        {
            RequireObject(workflow);
            var workflowName = workflow.Key;
            registrations.AddFlowWorkflow(workflowName);
            RegisterResources(registrations, workflow.GetSection("Resources"), workflowName, [], cancellationToken);
            foreach (var section in workflow.GetChildren())
            {
                if (section.Key.Equals("Resources", StringComparison.OrdinalIgnoreCase)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var name = section.Key;
                var componentAddress = ApplicationAddress.WorkflowComponent(workflowName, name);
                var address = componentAddress.Value;
                try
                {
                    RequireObject(section);
                    var type = RequiredString(section.GetSection("Type"));
                    var registration = registrations.LastOrDefault(item => item.IsKeyedService
                        && item.ServiceType == typeof(ComponentRegistration) && Equals(item.ServiceKey, type))
                        ?.KeyedImplementationInstance as ComponentRegistration
                        ?? throw new InvalidOperationException($"Component type '{type}' has no configuration registration.");
                    registration(registrations, section);
                    if (!registrations.Any(item => item.IsKeyedService &&
                        item.ServiceType == typeof(ComponentFactory)
                        && Equals(item.ServiceKey, address)))
                        throw new InvalidOperationException($"Registration did not provide a factory for '{address}'.");
                    HashSet<string> PortNames(Type type)
                    {
                        // Use the same declared addresses as runtime composition; unrelated
                        // host services of a native block type are not component ports.
                        var addresses = registrations.LastOrDefault(item => item.IsKeyedService
                            && item.ServiceType == typeof(IReadOnlyCollection<string>) && Equals(item.ServiceKey, type))
                            ?.KeyedImplementationInstance as IReadOnlyCollection<string> ?? [];
                        return addresses.Select(ApplicationAddress.Parse)
                            .Where(port => port.ComponentAddress == componentAddress)
                            .Select(port => port.PortName!).ToHashSet(StringComparer.Ordinal);
                    }
                    var inputs = PortNames(typeof(ITargetBlock<FlowMessage>));
                    var outputs = PortNames(typeof(ISourceBlock<FlowMessage>));
                    foreach (var property in section.GetChildren())
                    {
                        var portName = inputs.Concat(outputs).FirstOrDefault(port => port == property.Key);
                        if (portName is null)
                            portName = new[] { "Input", "Output", "Events" }.FirstOrDefault(port =>
                                port.Equals(property.Key, StringComparison.OrdinalIgnoreCase) && (inputs.Contains(port) || outputs.Contains(port)));
                        if (portName is null)
                        {
                            if (new[] { "Input", "Output", "Events" }.Contains(property.Key, StringComparer.OrdinalIgnoreCase))
                                throw new InvalidOperationException($"Component '{address}' has no data port '{property.Key}'.");
                            continue;
                        }
                        if (inputs.Contains(portName) && outputs.Contains(portName))
                            throw new InvalidOperationException($"Port property '{address}.{portName}' is ambiguous.");
                        LinkRegistration.RegisterPort(registrations, property,
                            ApplicationAddress.WorkflowPort(workflowName, name, portName), inputs.Contains(portName),
                            ports => ports.GetRequiredKeyedService<ApplicationConditionCompiler>(conditionSession));
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"Component '{address}' at '{section.Path}' could not be registered: {error.Message}", error);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        ((ServiceCollection)registrations).MakeReadOnly();
        services.Clear();
        foreach (var registration in registrations) services.Add(registration);
        return ValueTask.FromResult(services);
    }

    private static void RegisterResources(IServiceCollection services, IConfigurationSection section,
        string? workflow, string[] path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (path.Length == 0) RequireObject(section);
        if (path.Length > 0 && section.GetChildren().Any(child => child.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)))
        {
            var address = workflow is null ? ApplicationAddress.Resource(path) : ApplicationAddress.WorkflowResource(workflow, path);
            try
            {
                var type = RequiredString(section.GetSection("Type"));
                var register = services.LastOrDefault(item => item.IsKeyedService
                    && item.ServiceType == typeof(ResourceRegistration) && Equals(item.ServiceKey, type))
                    ?.KeyedImplementationInstance as ResourceRegistration
                    ?? throw new InvalidOperationException($"Resource type '{type}' has no configuration registration.");
                register(services, address, section);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Resource '{address}' at '{section.Path}' could not be registered: {error.Message}", error);
            }
            return;
        }
        foreach (var child in section.GetChildren())
        {
            if (path.Length == 0 && child.Key.Equals("Functions", StringComparison.OrdinalIgnoreCase))
                throw new CompositionConfigurationException($"'{child.Path}': functions are not supported.");
            var key = path.Length == 0 && child.Key.Equals("Connections", StringComparison.OrdinalIgnoreCase) ? "Connections" : child.Key;
            RegisterResources(services, child, workflow, [.. path, key], cancellationToken);
        }
    }

    private static string RequiredString(IConfigurationSection section)
        => !string.IsNullOrWhiteSpace(section.Value) && !section.GetChildren().Any()
            ? section.Value : throw new CompositionConfigurationException($"'{section.Path}' requires a non-empty scalar value.");

    private static void RequireObject(IConfigurationSection section)
    {
        if (section.Value is not null)
            throw new CompositionConfigurationException($"'{section.Path}' requires child sections, not a scalar value.");
    }

    /// <summary>Gets workflow.component from a component section, including a host-selected root.</summary>
    public static string GetComponentAddress(this IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var parts = section.Path.Split(ConfigurationPath.KeyDelimiter);
        if (parts.Length < 3 || !parts[^3].Equals("workflows", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{section.Path}' is not a workflow component section.", nameof(section));
        return ApplicationAddress.WorkflowComponent(parts[^2], parts[^1]).Value;
    }
}
