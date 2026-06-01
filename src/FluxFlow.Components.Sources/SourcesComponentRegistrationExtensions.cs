using FluxFlow.Components.Sources.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Sources;

public static class SourcesComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterSourcesComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterSourcesComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterSourcesComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<SourcesComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SourcesComponentOptions();
        configure(options);
        return registry.Register(new SourcesComponentModule(options));
    }
}
