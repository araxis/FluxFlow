using FluxFlow.Json.Model;

namespace FluxFlow.Composition;

/// <summary>Shared activation and physical-port checks for optional authoring paths.</summary>
internal static class ComponentActivation
{
    internal static async ValueTask<ComponentInstance> CreateAsync(
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
            throw new InvalidOperationException(
                $"Factory for component '{workflowName}.{componentName}' failed: {exception.Message}",
                exception);
        }

        if (instance is null)
        {
            throw new InvalidOperationException(
                $"Factory for component '{workflowName}.{componentName}' returned null.");
        }

        return instance;
    }

    internal static void ValidateInstance(
        string workflowName,
        string componentName,
        ComponentDescriptor descriptor,
        ComponentInstance instance)
    {
        if (instance.Inputs.Count != descriptor.Inputs.Count ||
            instance.Outputs.Count != descriptor.Outputs.Count)
        {
            throw new InvalidOperationException(
                $"Component '{workflowName}.{componentName}' instance ports do not exactly match its descriptor.");
        }

        foreach (var (name, metadata) in descriptor.Inputs)
        {
            if (!instance.Inputs.TryGetValue(name, out var input) ||
                input.Kind != metadata.Kind ||
                metadata.Kind == ComponentPortKind.Message && input.MessageType != metadata.MessageType)
            {
                throw new InvalidOperationException(
                    $"Component '{workflowName}.{componentName}' input '{name}' does not match its descriptor.");
            }
        }

        foreach (var (name, metadata) in descriptor.Outputs)
        {
            if (!instance.Outputs.TryGetValue(name, out var output) ||
                output.MessageType != metadata.MessageType)
            {
                throw new InvalidOperationException(
                    $"Component '{workflowName}.{componentName}' output '{name}' does not match its descriptor.");
            }
        }
    }
}
