using FluxFlow.Components.Projections.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Projections;

public static class ProjectionsComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterProjectionsComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterProjectionsComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterProjectionsComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<ProjectionsComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ProjectionsComponentOptions();
        configure(options);
        return registry.Register(new ProjectionsComponentModule(options));
    }
}
