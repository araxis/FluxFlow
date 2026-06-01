using FluxFlow.Components.Assertions.Options;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Assertions;

public static class AssertionsComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterAssertionsComponents(
        this RuntimeNodeFactoryRegistry registry,
        IFlowExpressionEngine expressionEngine)
        => registry.RegisterAssertionsComponents(options => options.UseExpressionEngine(expressionEngine));

    public static RuntimeNodeFactoryRegistry RegisterAssertionsComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<AssertionsComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AssertionsComponentOptions();
        configure(options);
        return registry.Register(new AssertionsComponentModule(options));
    }
}
