using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationRevisionRoute
{
    ApplicationAddress Source { get; }

    ApplicationAddress Target { get; }

    void TryDeliver(object message);
}

internal sealed class ApplicationRevisionRouting
{
    private Snapshot _current = Snapshot.Empty;

    public Snapshot Current => Volatile.Read(ref _current);

    public IReadOnlyList<IApplicationRevisionRoute> GetRoutes(ApplicationAddress source)
        => Current.Routes.TryGetValue(source, out var routes) ? routes : [];

    public void Swap(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _current, snapshot);
    }

    public sealed class Snapshot
    {
        public static Snapshot Empty { get; } = new(
            new Dictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>>(),
            new HashSet<RouteIdentity>());

        public Snapshot(
            IReadOnlyDictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>> routes,
            IReadOnlySet<RouteIdentity> identities)
        {
            Routes = routes;
            Identities = identities;
        }

        public IReadOnlyDictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>> Routes { get; }

        public IReadOnlySet<RouteIdentity> Identities { get; }

        public IReadOnlyList<ApplicationAddress> GetChangedSources(Snapshot next)
            => Identities
                .SymmetricExcept(next.Identities)
                .Select(static identity => identity.Source)
                .Distinct()
                .OrderBy(static address => address.Value, StringComparer.Ordinal)
                .ToArray();

        public IReadOnlyList<ApplicationAddress> GetChangedTargets(Snapshot next)
            => Identities
                .SymmetricExcept(next.Identities)
                .Select(static identity => identity.Target)
                .Distinct()
                .OrderBy(static address => address.Value, StringComparer.Ordinal)
                .ToArray();
    }

    public readonly record struct RouteIdentity(
        ApplicationAddress Source,
        ApplicationAddress Target,
        Type MessageType,
        string? ConditionExpression);
}

internal static class ApplicationRevisionRoutingSetExtensions
{
    public static IEnumerable<T> SymmetricExcept<T>(
        this IReadOnlySet<T> current,
        IReadOnlySet<T> next)
    {
        foreach (var value in current)
        {
            if (!next.Contains(value))
                yield return value;
        }

        foreach (var value in next)
        {
            if (!current.Contains(value))
                yield return value;
        }
    }
}
