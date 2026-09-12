using FluxFlow.Composition;
using FluxFlow.Json.Model;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeComponentActivator
{
    internal async ValueTask PopulateAsync(
        ApplicationDefinition definition,
        ComponentCatalog catalog,
        IServiceProvider services,
        IDictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent> components,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var (componentName, component) in workflow.Components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!catalog.TryGetDescriptor(component.Type, out var descriptor))
                {
                    throw new ApplicationRuntimeAssemblerException(
                        $"Component '{workflowName}.{componentName}' uses unregistered type '{component.Type}'.");
                }

                ComponentInstance instance;
                try
                {
                    instance = await descriptor.Factory(new ComponentActivationContext(
                        services, workflowName, componentName, component)).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Factory for component '{workflowName}.{componentName}' returned null.");
                }
                catch (Exception exception) when (exception is not AggregateException)
                {
                    throw new ApplicationRuntimeAssemblerException(exception.Message, exception);
                }
                components.Add(
                    new ApplicationRuntimeComponentKey(workflowName, componentName),
                    new ApplicationRuntimeBuiltComponent(descriptor, instance));
            }
        }
    }
}

internal readonly record struct ApplicationRuntimeComponentKey(
    string WorkflowName,
    string ComponentName);

internal sealed record ApplicationRuntimeBuiltComponent(
    ComponentDescriptor Descriptor,
    ComponentInstance Instance);
