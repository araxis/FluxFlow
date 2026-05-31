namespace FluxFlow.Engine.Runtime;

public static class RuntimeNodeFactoryRegistryExtensions
{
    public static RuntimeNodeFactoryRegistry Register(
        this RuntimeNodeFactoryRegistry registry,
        IFlowNodeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(registration);
        return registry.Register(registration.Type, registration.Create);
    }

    public static RuntimeNodeFactoryRegistry RegisterRange(
        this RuntimeNodeFactoryRegistry registry,
        IEnumerable<IFlowNodeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (var registration in registrations)
        {
            registry.Register(registration);
        }

        return registry;
    }
}
