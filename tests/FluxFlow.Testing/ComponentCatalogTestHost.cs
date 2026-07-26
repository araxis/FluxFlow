using FluxFlow.Composition;
using FluxFlow.Components.Designer;
using FluxFlow.Engine.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Testing;

public static class ComponentCatalogTestHost
{
    public static ComponentCatalog Create(Action<IServiceCollection> addComponents)
    {
        ArgumentNullException.ThrowIfNull(addComponents);

        var services = new ServiceCollection();
        services.AddFluxFlowEngine();
        addComponents(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ComponentCatalog>();
    }

    public static ComponentDesignMetadataCatalog CreateDesignMetadataCatalog(
        Action<IServiceCollection> addComponents)
    {
        ArgumentNullException.ThrowIfNull(addComponents);

        var services = new ServiceCollection();
        addComponents(services);
        services.AddComponentDesignMetadataCatalog();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ComponentDesignMetadataCatalog>();
    }
}
