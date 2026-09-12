using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Core;
using FluxFlow.Nodes;
using System.Collections.Immutable;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationRevisionRoute : IFlowOutputRoute<FlowMessage>
{
    ApplicationAddress Source { get; }

    ApplicationAddress Target { get; }

    bool IsFallback => false;

    bool IsMatch(FlowMessage message);

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
            IReadOnlySet<RouteIdentity> identities,
            IEnumerable<ApplicationAddress>? discardedOutputs = null,
            IEnumerable<ApplicationAddress>? replyOutputs = null)
        {
            Routes = routes;
            Identities = identities;
            DiscardedOutputs = (discardedOutputs ?? []).ToImmutableHashSet();
            ReplyOutputs = (replyOutputs ?? []).ToImmutableHashSet();
        }

        public IReadOnlyDictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>> Routes { get; }

        public IReadOnlySet<RouteIdentity> Identities { get; }

        public IReadOnlySet<ApplicationAddress> DiscardedOutputs { get; }
        public IReadOnlySet<ApplicationAddress> ReplyOutputs { get; }

        public IReadOnlyList<ApplicationAddress> GetChangedSources(Snapshot next)
            => Identities
                .SymmetricExcept(next.Identities)
                .Select(static identity => identity.Source)
                .Concat(DiscardedOutputs.SymmetricExcept(next.DiscardedOutputs))
                .Concat(ReplyOutputs.SymmetricExcept(next.ReplyOutputs))
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
        string? ConditionExpression,
        FlowDeliveryPolicy Delivery = FlowDeliveryPolicy.Reliable,
        bool IsFallback = false)
    {
        // Expression text cannot establish equivalence of bound parameters or host code.
        internal CompiledApplicationLink? Condition { get; init; }
    }
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
