using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>Selects graph routes; the component output owns scheduling and fanout.</summary>
internal sealed class FlowRouter : IAsyncDisposable
{
    private readonly FlowOutputEndpoint _source;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly bool _ownsOutput;
    private readonly FlowLink[] _routes;
    private readonly bool[] _selected;
    private bool _isolatedFailure;
    private readonly bool _discard;
    private readonly Action<FlowMessage>? _reply;
    private readonly Predicate<FlowMessage>? _replyCondition;
    private readonly List<IDisposable> _links = [];
    private readonly object _linkGate = new();
    private IDisposable? _failureDrain;
    private bool _detached;

    public FlowRouter(FlowOutputEndpoint output, IEnumerable<FlowLink> routes, bool discard,
        int capacity, Action<Exception> fault,
        Action<FlowMessage>? reply = null, Predicate<FlowMessage>? replyCondition = null)
    {
        _source = output;
        _routes = routes.ToArray();
        _selected = new bool[_routes.Length];
        _discard = discard;
        _reply = reply;
        _replyCondition = replyCondition;
        _ownsOutput = output.Block is not FlowOutput<FlowMessage>;
        _output = output.Block as FlowOutput<FlowMessage>
            ?? new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = capacity }, capacityIncludesActive: true);
        _output.ConfigureRouting(SelectRoutes, fault);
        try
        {
            for (var index = 0; index < _routes.Length; index++)
            {
                var selectedIndex = index;
                var route = _routes[index];
                _links.Add(_output.LinkRoute(route.Input.Block, _ => _selected[selectedIndex],
                    route.Delivery != FlowDeliveryPolicy.Reliable,
                    $"Target '{route.Input.Address}' rejected a message from '{output.Address}'.",
                    route.Delivery == FlowDeliveryPolicy.IsolateRejection ? route.ReportRejection : null));
            }
        }
        catch
        {
            Detach();
            throw;
        }
    }

    public Task Completion => _output.Completion;

    public void ConnectSource()
    {
        lock (_linkGate)
        {
            if (_ownsOutput && !_detached)
                _links.Add(_source.Block.LinkTo((ITargetBlock<FlowMessage>)_output.Ingress,
                    new DataflowLinkOptions { PropagateCompletion = true }));
        }
    }

    public void Fault(Exception error)
    {
        _output.Fault(error);
        lock (_linkGate)
        {
            // A native source already completing may ignore Fault while retaining
            // queued output. Release that tail without invoking business routes.
            if (_ownsOutput && !_detached && !_source.Block.Completion.IsCompleted)
                _failureDrain ??= _source.Block.LinkTo(DataflowBlock.NullTarget<FlowMessage>(),
                    new DataflowLinkOptions { Append = false });
        }
    }

    public void Detach()
    {
        lock (_linkGate)
        {
            if (_detached) return;
            _detached = true;
            foreach (var link in _links) link.Dispose();
            _links.Clear();
            _failureDrain?.Dispose();
            _output.ClearRouting();
            if (_ownsOutput) _output.Complete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Detach();
        if (_ownsOutput) await _output.DisposeAsync().ConfigureAwait(false);
    }

    private void SelectRoutes(FlowMessage message)
    {
        try
        {
            _isolatedFailure = false;
            var selection = FlowRouteSelection.SelectOrdinary(_routes, _selected, (Owner: this, Message: message),
                static route => route.IsFallback, static (route, state) => state.Owner.IsMatch(route, state.Message));

            var isReply = _reply is not null && (_replyCondition is null || _replyCondition(message));
            if (isReply) _reply!(message);
            FlowRouteSelection.Complete(_selected, selection, _source.Address, _discard || _isolatedFailure, isReply);
        }
        catch
        {
            // A partially evaluated route set must never leak into delivery.
            Array.Clear(_selected);
            throw;
        }
    }

    private bool IsMatch(FlowLink route, FlowMessage message)
    {
        try { return route.Condition is null || route.Condition(message); }
        catch (Exception error) when (route.Delivery == FlowDeliveryPolicy.IsolateRejection)
        {
            _isolatedFailure = true;
            route.ReportRejection(message, error);
            return false;
        }
    }
}
