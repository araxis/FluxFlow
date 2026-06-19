using FluxFlow.Components.Routing.Options;
using FluxFlow.Mapping;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Routing;

public static class RoutingComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterRoutingComponents(
        this RuntimeNodeFactoryRegistry registry,
        IFlowExpressionEngine expressionEngine)
        => registry.RegisterRoutingComponents(options => options.UseExpressionEngine(expressionEngine));

    public static RuntimeNodeFactoryRegistry RegisterRoutingComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<RoutingComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RoutingComponentOptions();
        configure(options);
        return registry.Register(new RoutingComponentModule(options));
    }
}
