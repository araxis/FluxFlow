using FluxFlow.Components.Expectations.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Expectations;

public static class ExpectationsComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterExpectationsComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterExpectationsComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterExpectationsComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<ExpectationsComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ExpectationsComponentOptions();
        configure(options);
        return registry.Register(new ExpectationsComponentModule(options));
    }
}
