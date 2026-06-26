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
            return exact;

        var matches = _factories
            .Where(candidate => candidate.Key.IsAssignableFrom(inputType))
            .ToArray();

        if (matches.Length == 0)
            return _defaultFactory;

        var bestMatches = matches
            .Where(candidate => matches.All(other =>
                candidate.Key == other.Key || other.Key.IsAssignableFrom(candidate.Key)))
            .ToArray();

        if (bestMatches.Length == 1)
            return bestMatches[0].Value;

        var candidateNames = matches
            .Select(candidate => candidate.Key.FullName ?? candidate.Key.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        throw new InvalidOperationException(
            $"Context factory resolution for input type '{inputType}' is ambiguous: " +
            $"registrations for {string.Join(", ", candidateNames)} match and no single registration is more specific. " +
            $"Register a context factory for '{inputType}' to disambiguate.");
    }
}
