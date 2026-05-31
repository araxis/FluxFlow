using FluxFlow.Components.Mapping.Options;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mapping;

public static class MappingComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterMappingComponents(
        this RuntimeNodeFactoryRegistry registry,
        IFlowExpressionEngine expressionEngine)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        return registry.RegisterMappingComponents(options => options.UseExpressionEngine(expressionEngine));
    }

    public static RuntimeNodeFactoryRegistry RegisterMappingComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<MappingComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MappingComponentOptions();
        configure(options);
        return registry.Register(new MappingComponentModule(options));
    }
}
