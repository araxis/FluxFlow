using FluxFlow.Composition;
using FluxFlow.Composition.Model;
using FluxFlow.Components.Designer;
using FluxFlow.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Testing;

public static class ComponentCatalogTestHost
{
    public static ComponentCatalog Create(Action<IServiceCollection> addComponents)
    {
        ArgumentNullException.ThrowIfNull(addComponents);

        var services = new ServiceCollection();
        services.AddFluxFlow(
            new ApplicationDefinition(),
            options => options.StartWithHost = false);
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
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ComponentDesignMetadataCatalog>();
    }
}
