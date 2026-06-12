namespace FluxFlow.Components.Expressions;

public sealed class FlowContextFactoryRegistry<TFactory>
    where TFactory : class
{
    private readonly Dictionary<Type, TFactory> _factories = [];
    private TFactory _defaultFactory;

    public FlowContextFactoryRegistry(TFactory defaultFactory)
        => _defaultFactory = defaultFactory ?? throw new ArgumentNullException(nameof(defaultFactory));

    public FlowContextFactoryRegistry<TFactory> UseDefault(TFactory factory)
    {
        _defaultFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public FlowContextFactoryRegistry<TFactory> Register<TInput>(TFactory factory)
        => Register(typeof(TInput), factory);

    public FlowContextFactoryRegistry<TFactory> Register(Type inputType, TFactory factory)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentNullException.ThrowIfNull(factory);

        _factories[inputType] = factory;
        return this;
    }

    public TFactory Resolve(Type inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        if (_factories.TryGetValue(inputType, out var exact))
        {
            return exact;
        }

        Type? bestType = null;
        TFactory? bestFactory = null;

        foreach (var (candidateType, factory) in _factories)
        {
            if (!candidateType.IsAssignableFrom(inputType))
            {
                continue;
            }

            if (bestType is null || bestType.IsAssignableFrom(candidateType))
            {
                bestType = candidateType;
                bestFactory = factory;
            }
        }

        if (bestType is null)
        {
            return _defaultFactory;
        }

        foreach (var (candidateType, _) in _factories)
        {
            if (candidateType.IsAssignableFrom(inputType) &&
                !candidateType.IsAssignableFrom(bestType))
            {
                throw new InvalidOperationException(
                    $"Context factory resolution for input type '{inputType}' is ambiguous: " +
                    $"registrations for '{bestType}' and '{candidateType}' both match and neither is more specific. " +
                    $"Register a context factory for '{inputType}' to disambiguate.");
            }
        }

        return bestFactory!;
    }
}
