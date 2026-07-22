using FluxFlow.Composition;
using FluxFlow.Composition.Model;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeComponentActivator(CompositionNodeRegistry registry)
{
    internal async ValueTask PopulateAsync(
        ApplicationDefinition definition,
        IServiceProvider services,
        IDictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent> components,
        CancellationToken cancellationToken)
    {
        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var (componentName, component) in workflow.Components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!registry.TryGetRegistration(component.Type, out var registration))
                {
                    throw new ApplicationRuntimeAssemblerException(
                        $"Component '{workflowName}.{componentName}' uses unregistered type '{component.Type}'.");
                }

                var descriptor = await CreateAsync(
                        workflowName,
                        componentName,
                        component,
                        registration,
                        services)
                    .ConfigureAwait(false);
                components.Add(
                    new ApplicationRuntimeComponentKey(workflowName, componentName),
                    new ApplicationRuntimeBuiltComponent(registration, descriptor));
            }
        }
    }

    private static async ValueTask<ComposedNode> CreateAsync(
        string workflowName,
        string componentName,
        ComponentDefinition definition,
        CompositionNodeRegistration registration,
        IServiceProvider services)
    {
        ComposedNode descriptor;
        try
        {
            descriptor = await registration.Factory(new CompositionNodeFactoryContext(
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

        if (descriptor is null)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Factory for component '{workflowName}.{componentName}' returned null.");
        }

        try
        {
            ValidateDescriptor(workflowName, componentName, registration, descriptor);
            return descriptor;
        }
        catch (Exception validationFailure)
        {
            try
            {
                await descriptor.DisposeAsync().ConfigureAwait(false);
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

    private static void ValidateDescriptor(
        string workflowName,
        string componentName,
        CompositionNodeRegistration registration,
        ComposedNode descriptor)
    {
        if (descriptor.Inputs.Count != registration.Inputs.Count ||
            descriptor.Outputs.Count != registration.Outputs.Count)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Component '{workflowName}.{componentName}' descriptor ports do not exactly match its registration.");
        }

        foreach (var (name, metadata) in registration.Inputs)
        {
            if (!descriptor.Inputs.TryGetValue(name, out var input) ||
                input.Kind != metadata.Kind ||
                metadata.Kind == CompositionPortKind.Message && input.MessageType != metadata.MessageType)
            {
                throw new ApplicationRuntimeAssemblerException(
                    $"Component '{workflowName}.{componentName}' input '{name}' does not match its registration.");
            }
        }

        foreach (var (name, metadata) in registration.Outputs)
        {
            if (!descriptor.Outputs.TryGetValue(name, out var output) ||
                output.MessageType != metadata.MessageType)
            {
                throw new ApplicationRuntimeAssemblerException(
                    $"Component '{workflowName}.{componentName}' output '{name}' does not match its registration.");
            }
        }
    }
}

internal readonly record struct ApplicationRuntimeComponentKey(
    string WorkflowName,
    string ComponentName);

internal sealed record ApplicationRuntimeBuiltComponent(
    CompositionNodeRegistration Registration,
    ComposedNode Descriptor);
