using FluxFlow.Components.Metrics.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Metrics;

public static class MetricsComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterMetricsComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterMetricsComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterMetricsComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<MetricsComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MetricsComponentOptions();
        configure(options);
        return registry.Register(new MetricsComponentModule(options));
    }
}
