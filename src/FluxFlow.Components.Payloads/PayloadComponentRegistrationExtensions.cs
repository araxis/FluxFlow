using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Payloads;

public static class PayloadComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterPayloadComponents(
        this RuntimeNodeFactoryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Register(new PayloadComponentModule());
    }
}
