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

    public static RuntimeNodeFactoryRegistry Register(
        this RuntimeNodeFactoryRegistry registry,
        IFlowNodeModule module)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(module);
        return registry.RegisterRange(module.Registrations);
    }

    public static RuntimeNodeFactoryRegistry RegisterRange(
        this RuntimeNodeFactoryRegistry registry,
        IEnumerable<IFlowNodeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(registrations);

        var validated = ValidateRegistrations(registry, registrations);
        foreach (var registration in validated)
        {
            registry.Register(registration);
        }

        return registry;
    }

    public static RuntimeNodeFactoryRegistry RegisterModules(
        this RuntimeNodeFactoryRegistry registry,
        IEnumerable<IFlowNodeModule> modules)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(modules);

        var registrations = new List<IFlowNodeRegistration>();
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            registrations.AddRange(module.Registrations);
        }

        return registry.RegisterRange(registrations);
    }

    private static IReadOnlyList<IFlowNodeRegistration> ValidateRegistrations(
        RuntimeNodeFactoryRegistry registry,
        IEnumerable<IFlowNodeRegistration> registrations)
    {
        var validated = registrations
            .Select(registration => registration ?? throw new ArgumentException(
                "A flow node registration set cannot contain a null registration.",
                nameof(registrations)))
            .ToArray();

        var duplicate = validated
            .GroupBy(registration => registration.Type)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"A flow node registration set contains more than one factory for '{duplicate.Key}'.");
        }

        var existing = validated
            .FirstOrDefault(registration => registry.Factories.ContainsKey(registration.Type));

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"A flow node factory is already registered for '{existing.Type}'.");
        }

        return validated;
    }
}
