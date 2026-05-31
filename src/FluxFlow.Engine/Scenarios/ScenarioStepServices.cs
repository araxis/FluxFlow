namespace FluxFlow.Engine.Scenarios;

public sealed class ScenarioStepServices
{
    private readonly IReadOnlyDictionary<Type, object> _services;

    public static ScenarioStepServices Empty { get; } = new();

    public ScenarioStepServices()
        : this(new Dictionary<Type, object>())
    {
    }

    private ScenarioStepServices(IReadOnlyDictionary<Type, object> services)
    {
        _services = services;
    }

    public ScenarioStepServices Add<TService>(TService service)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(service);

        var copy = new Dictionary<Type, object>(_services)
        {
            [typeof(TService)] = service
        };

        return new ScenarioStepServices(copy);
    }

    public bool TryGet<TService>(out TService service)
        where TService : class
    {
        if (_services.TryGetValue(typeof(TService), out var value) &&
            value is TService typed)
        {
            service = typed;
            return true;
        }

        service = null!;
        return false;
    }

    public TService GetRequired<TService>()
        where TService : class
        => TryGet<TService>(out var service)
            ? service
            : throw new InvalidOperationException(
                $"Scenario service '{typeof(TService).Name}' is not available.");
}
