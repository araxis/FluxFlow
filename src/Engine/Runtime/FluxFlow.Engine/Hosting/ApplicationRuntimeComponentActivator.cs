using FluxFlow.Composition.Definition;
using FluxFlow.Composition;
using FluxFlow.Json.Model;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeComponentActivator
{
    internal async ValueTask PopulateAsync(
        ApplicationDefinition definition,
        IReadOnlyDictionary<string, ComponentFactory> catalog,
        IServiceProvider services,
        IDictionary<ApplicationRuntimeComponentKey, ComponentInstance> components,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var (componentName, component) in workflow.Components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!catalog.TryGetValue(component.Type, out var descriptor))
                {
                    throw new ApplicationRuntimeAssemblerException(
                        $"Component '{workflowName}.{componentName}' uses unknown type '{component.Type}'.");
                }

                ComponentInstance instance;
                try
                {
                    instance = await descriptor(new ComponentActivationContext(
                        services, workflowName, componentName, component)).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Factory for component '{workflowName}.{componentName}' returned null.");
                }
                catch (Exception exception) when (exception is not AggregateException)
                {
                    throw new ApplicationRuntimeAssemblerException(exception.Message, exception);
                }
                components.Add(
                    new ApplicationRuntimeComponentKey(workflowName, componentName),
                    instance);
            }
        }
    }
}

internal readonly record struct ApplicationRuntimeComponentKey(
    string WorkflowName,
    string ComponentName);
