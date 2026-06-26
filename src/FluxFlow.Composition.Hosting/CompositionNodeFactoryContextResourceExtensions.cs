using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition.Hosting;

public static class CompositionNodeFactoryContextResourceExtensions
{
    public static string GetRequiredResourceKey(
        this CompositionNodeFactoryContext context,
        string resourceName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var name = resourceName.Trim();
        if (context.Resources.TryGetValue(name, out var key)
            && !string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        throw new InvalidOperationException(
            $"Node '{context.WorkflowName}.{context.NodeName}' requires resource '{name}', but no resource reference was configured.");
    }

    public static TResource GetRequiredResource<TResource>(
        this CompositionNodeFactoryContext context,
        string resourceName)
        where TResource : notnull
    {
        var key = context.GetRequiredResourceKey(resourceName);
        var name = resourceName.Trim();
        try
        {
            return context.Services.GetRequiredKeyedService<TResource>(key);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Node '{context.WorkflowName}.{context.NodeName}' resource '{name}' references '{key}', but no keyed service of type '{typeof(TResource).Name}' is registered.",
                exception);
        }
    }

    public static TResource? GetResource<TResource>(
        this CompositionNodeFactoryContext context,
        string resourceName)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var name = resourceName.Trim();
        if (!context.Resources.TryGetValue(name, out var key)
            || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return context.Services.GetKeyedService<TResource>(key.Trim());
    }
}
