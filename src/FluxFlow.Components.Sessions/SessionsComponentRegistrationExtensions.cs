using FluxFlow.Components.Sessions.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Sessions;

public static class SessionsComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterSessionsComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterSessionsComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterSessionsComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<SessionsComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SessionsComponentOptions();
        configure(options);
        return registry.Register(new SessionsComponentModule(options));
    }
}
