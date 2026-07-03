using FluxFlow.Composition;

namespace FluxFlow.Composition.Hosting;

/// <summary>
/// Legacy location of the keyed-resource helpers. The implementations now live as
/// instance methods on <see cref="CompositionNodeFactoryContext"/> so composition
/// adapters do not need a FluxFlow.Composition.Hosting reference.
/// </summary>
public static class CompositionNodeFactoryContextResourceExtensions
{
    [Obsolete("Use CompositionNodeFactoryContext.GetRequiredResourceKey instead.")]
    public static string GetRequiredResourceKey(
        this CompositionNodeFactoryContext context,
        string resourceName)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredResourceKey(resourceName);
    }

    [Obsolete("Use CompositionNodeFactoryContext.GetRequiredResource instead.")]
    public static TResource GetRequiredResource<TResource>(
        this CompositionNodeFactoryContext context,
        string resourceName)
        where TResource : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredResource<TResource>(resourceName);
    }

    [Obsolete("Use CompositionNodeFactoryContext.GetResource instead.")]
    public static TResource? GetResource<TResource>(
        this CompositionNodeFactoryContext context,
        string resourceName)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetResource<TResource>(resourceName);
    }
}
