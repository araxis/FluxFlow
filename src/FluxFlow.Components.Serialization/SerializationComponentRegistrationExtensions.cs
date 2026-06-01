using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Serialization;

public static class SerializationComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterSerializationComponents(
        this RuntimeNodeFactoryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Register(new SerializationComponentModule());
    }
}
