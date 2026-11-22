using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Authoring.Definitions;

public static class DesignerComponentRegistration
{
    public static IServiceCollection AddCatalogComponents(this IServiceCollection services, Plugins.PluginDescription[] plugins)
    {
        foreach (var component in plugins.SelectMany(p => p.Components))
            services.AddSingleton<IComponentDescription>(component);
        return services;
    }
}
