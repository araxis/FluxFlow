using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Timers;

public static class TimerComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterTimerComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterTimerComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterTimerComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<TimerComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TimerComponentOptions();
        configure(options);
        return registry.Register(new TimerComponentModule(options));
    }
}
