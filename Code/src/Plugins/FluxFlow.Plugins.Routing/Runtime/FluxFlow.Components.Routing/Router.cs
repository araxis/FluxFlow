using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Routing;
/// <summary>Sends messages to all matching routes, or Unmatched if none match. Output carries errors.</summary>
public sealed class Router : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    private readonly Predicate<FlowMessage>[] _routes;
    private readonly BroadcastBlock<FlowMessage>[] _outputs;
    private readonly BroadcastBlock<FlowMessage> _unmatched;
    public ISourceBlock<FlowMessage> Unmatched => _unmatched;
    public IReadOnlyList<ISourceBlock<FlowMessage>> Outputs { get; }

    public Router(RouterOptions options)
    {
        _routes = Snapshot(options);
        var execution = options.Execution ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        _unmatched = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        _outputs = _routes.Select(_ => new BroadcastBlock<FlowMessage>(static message => message, outputOptions)).ToArray();
        Outputs = Array.AsReadOnly<ISourceBlock<FlowMessage>>(_outputs);
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasOutput(_unmatched);
        foreach (var output in _outputs) Lifetime.HasOutput(output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var matches = new bool[_routes.Length];
        var matched = false;
        for (var i = 0; i < _routes.Length; i++)
            matched |= matches[i] = _routes[i](message);
        if (!matched)
        {
            await EmitAsync(_unmatched, message, Stopping).ConfigureAwait(false);
            return[];
        }

        for (var i = 0; i < matches.Length; i++)
            if (matches[i])
                await EmitAsync(_outputs[i], message, Stopping).ConfigureAwait(false);
        return[];
    }

    private static Predicate<FlowMessage>[] Snapshot(RouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Routes);
        var routes = options.Routes.ToArray();
        if (routes.Any(route => route is null))
            throw new ArgumentException("Routes cannot contain null predicates.", nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity, nameof(options.OutputCapacity));
        return routes;
    }
}
