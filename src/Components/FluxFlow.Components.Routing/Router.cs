using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Routing;

/// <summary>Sends messages to all matching routes, or Unmatched if none match. Output carries errors.</summary>
public sealed class Router : FlowTransformNode
{
    private readonly Predicate<FlowMessage>[] _routes;
    private readonly FlowOutput<FlowMessage>[] _outputs;
    private readonly FlowOutput<FlowMessage> _unmatched;
    public ISourceBlock<FlowMessage> Unmatched => _unmatched;
    public IReadOnlyList<ISourceBlock<FlowMessage>> Outputs { get; }

    public Router(RouterOptions options) : this(options, Snapshot(options)) { }

    private Router(RouterOptions options, Predicate<FlowMessage>[] routes)
        : base(options.Execution, options.OutputCapacity)
    {
        _routes = routes;
        _unmatched = AddOutput<FlowMessage>();
        _outputs = routes.Select(_ => AddOutput<FlowMessage>()).ToArray();
        Outputs = Array.AsReadOnly<ISourceBlock<FlowMessage>>(_outputs);
    }

    protected override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var matches = new bool[_routes.Length];
        var matched = false;
        for (var i = 0; i < _routes.Length; i++)
            matched |= matches[i] = _routes[i](message);
        if (!matched)
        {
            await EmitAsync(_unmatched, message, Stopping).ConfigureAwait(false);
            return [];
        }
        for (var i = 0; i < matches.Length; i++)
            if (matches[i]) await EmitAsync(_outputs[i], message, Stopping).ConfigureAwait(false);
        return [];
    }

    private static Predicate<FlowMessage>[] Snapshot(RouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Routes);
        var routes = options.Routes.ToArray();
        if (routes.Any(route => route is null)) throw new ArgumentException("Routes cannot contain null predicates.", nameof(options));
        return routes;
    }
}
