using FluxFlow.Core.Execution;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Core.Execution;

/// <summary>Selects graph routes; the component output owns scheduling and fanout.</summary>
internal sealed class FlowRouter : IAsyncDisposable
{
    private readonly ISourceBlock<FlowMessage> _source;
    private readonly string _address;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly bool _ownsOutput;
    private readonly FlowLink[] _routes;
    private readonly bool[] _selected;
    private bool _isolatedFailure;
    private readonly List<IDisposable> _links = [];
    private readonly object _linkGate = new();
    private IDisposable? _failureDrain;
    private bool _detached;

    public FlowRouter(string address, ISourceBlock<FlowMessage> source,
        IEnumerable<FlowLink> routes, int capacity, Action<Exception> fault)
    {
        _source = source;
        _address = address;
        _routes = routes.ToArray();
        _selected = new bool[_routes.Length];
        _ownsOutput = source is not FlowOutput<FlowMessage>;
        _output = source as FlowOutput<FlowMessage>
            ?? new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = capacity }, capacityIncludesActive: true);
        _output.ConfigureRouting(SelectRoutes, fault);
        try
        {
            for (var index = 0; index < _routes.Length; index++)
            {
                var selectedIndex = index;
                var route = _routes[index];
                _links.Add(_output.LinkRoute(route.Input, _ => _selected[selectedIndex],
                    route.Delivery != FlowDeliveryPolicy.Reliable,
                    $"Target '{route.InputAddress}' rejected a message from '{address}'.",
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
                _links.Add(_source.LinkTo((ITargetBlock<FlowMessage>)_output.Ingress,
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
            if (_ownsOutput && !_detached && !_source.Completion.IsCompleted)
                _failureDrain ??= _source.LinkTo(DataflowBlock.NullTarget<FlowMessage>(),
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

            FlowRouteSelection.Complete(_selected, selection, _address,
                _isolatedFailure);
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
