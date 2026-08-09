using FluxFlow.Composition;
using FluxFlow.Composition.Model;

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

                var instance = await CreateAsync(
                        workflowName,
                        componentName,
                        component,
                        descriptor,
                        services)
                    .ConfigureAwait(false);
                components.Add(
                    new ApplicationRuntimeComponentKey(workflowName, componentName),
                    new ApplicationRuntimeBuiltComponent(descriptor, instance));
            }
        }
    }

    private static async ValueTask<ComponentInstance> CreateAsync(
        string workflowName,
        string componentName,
        ComponentDefinition definition,
        ComponentDescriptor descriptor,
        IServiceProvider services)
    {
        ComponentInstance instance;
        try
        {
            instance = await descriptor.Factory(new ComponentActivationContext(
                    services,
                    workflowName,
                    componentName,
                    definition))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Factory for component '{workflowName}.{componentName}' failed: {exception.Message}",
                exception);
        }

        if (instance is null)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Factory for component '{workflowName}.{componentName}' returned null.");
        }

        try
        {
            ValidateInstance(workflowName, componentName, descriptor, instance);
            return instance;
        }
        catch (Exception validationFailure)
        {
            try
            {
                await instance.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Component '{workflowName}.{componentName}' validation and cleanup failed.",
                    validationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    private static void ValidateInstance(
        string workflowName,
        string componentName,
        ComponentDescriptor descriptor,
        ComponentInstance instance)
    {
        if (instance.Inputs.Count != descriptor.Inputs.Count ||
            instance.Outputs.Count != descriptor.Outputs.Count)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Component '{workflowName}.{componentName}' instance ports do not exactly match its descriptor.");
        }

        foreach (var (name, metadata) in descriptor.Inputs)
        {
            if (!instance.Inputs.TryGetValue(name, out var input) ||
                input.Kind != metadata.Kind ||
                metadata.Kind == ComponentPortKind.Message && input.MessageType != metadata.MessageType)
            {
                throw new ApplicationRuntimeAssemblerException(
                    $"Component '{workflowName}.{componentName}' input '{name}' does not match its descriptor.");
            }
        }

        foreach (var (name, metadata) in descriptor.Outputs)
        {
            if (!instance.Outputs.TryGetValue(name, out var output) ||
                output.MessageType != metadata.MessageType)
            {
                throw new ApplicationRuntimeAssemblerException(
                    $"Component '{workflowName}.{componentName}' output '{name}' does not match its descriptor.");
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
