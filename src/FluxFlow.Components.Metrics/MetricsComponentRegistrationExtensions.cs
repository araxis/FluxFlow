using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Metrics;

public static class MetricsComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterMetricsComponents(
        this RuntimeNodeFactoryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Register(new MetricsComponentModule());
    }
}
