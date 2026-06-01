using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Control;

public static class ControlComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterControlComponents(
        this RuntimeNodeFactoryRegistry registry,
        IFlowExpressionEngine expressionEngine)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        return registry.RegisterControlComponents(options => options.UseExpressionEngine(expressionEngine));
    }

    public static RuntimeNodeFactoryRegistry RegisterControlComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<ControlComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ControlComponentOptions();
        configure(options);
        return registry.Register(new ControlComponentModule(options));
    }
}
