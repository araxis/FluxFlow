using FluxFlow.Components.State.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.State;

public static class StateComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterStateComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterStateComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterStateComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<StateComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new StateComponentOptions();
        configure(options);
        return registry.Register(new StateComponentModule(options));
    }
}
