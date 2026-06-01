using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Http;

public static class HttpComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterHttpComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterHttpComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterHttpComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<HttpComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HttpComponentOptions();
        configure(options);
        return registry.Register(new HttpComponentModule(options));
    }
}
