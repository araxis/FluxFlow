using FluxFlow.Components.Observability.Options;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Observability;

public static class ObservabilityComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterObservabilityComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterObservabilityComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterObservabilityComponents(
        this RuntimeNodeFactoryRegistry registry,
        IFlowExpressionEngine expressionEngine)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        return registry.RegisterObservabilityComponents(
            options => options.UseExpressionEngine(expressionEngine));
    }

    public static RuntimeNodeFactoryRegistry RegisterObservabilityComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<ObservabilityComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ObservabilityComponentOptions();
        configure(options);
        return registry.Register(new ObservabilityComponentModule(options));
    }
}
