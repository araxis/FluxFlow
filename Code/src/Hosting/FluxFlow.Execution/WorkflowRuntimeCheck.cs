using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Execution;

internal static class WorkflowRuntimeCheck
{
    public static void Validate(string json, CancellationToken cancellationToken, Plugins.InstalledPlugins? plugins = null)
    {
        using var configuration = WorkflowExecution.ReadConfiguration(json);
        Validate(configuration, cancellationToken, plugins);
    }

    internal static void Validate(IConfiguration configuration, CancellationToken cancellationToken, Plugins.InstalledPlugins? plugins)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var services = WorkflowExecution.RegisterComponents(new ServiceCollection(), plugins);
        var errors = new List<string>();
        foreach (var workflow in configuration.GetSection("Workflows").GetChildren())
        foreach (var component in workflow.GetChildren())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (component.Key.Equals("Resources", StringComparison.OrdinalIgnoreCase)) continue;
            var type = component["Type"];
            var address = component.GetComponentAddress();
            try
            {
                var register = Find<ComponentRegistration>(services, type);
                if (register is null)
                {
                    var requirement = plugins?.Modules.SelectMany(module => module.Description.Components)
                        .FirstOrDefault(component => component.Palette.ComponentType == type)?.HostRequirement
                        ?? "Use a host that registers this component type.";
                    throw new InvalidOperationException($"Not available in this companion. {requirement}");
                }
                Find<ComponentConfigurationCheck>(services, type)?.Invoke(configuration, component);

            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                errors.Add($"Component '{address}' ({type ?? "missing Type"}): {error.Message}");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        // Use the startup registrars without resolving a provider or activating any factory.
        WorkflowExecution.Register(new ServiceCollection(), configuration, plugins);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static T? Find<T>(IServiceCollection services, string? key) where T : Delegate =>
        services.LastOrDefault(item => item.IsKeyedService && item.ServiceType == typeof(T)
            && Equals(item.ServiceKey, key))?.KeyedImplementationInstance as T;
}
