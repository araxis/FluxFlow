using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Application;

/// <summary>Resolves resource references through native keyed DI.</summary>
public static class FlowResourceExtensions
{
    /// <summary>Resolves relative resource addresses, then an explicit host key.</summary>
    public static TResource GetRequiredFlowResource<TResource>(this IServiceProvider services,
        string reference, string? workflowName = null) where TResource : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        foreach (var address in ApplicationAddress.ResolveResourcePaths(reference.Trim(), workflowName))
            if (services.GetKeyedService<TResource>(address.Value) is { } value) return value;
        return services.GetKeyedService<TResource>(reference.Trim())
            ?? throw new InvalidOperationException($"Resource '{reference}' of type '{typeof(TResource).Name}' is not registered.");
    }
}
