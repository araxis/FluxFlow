using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.State.Contracts;

namespace FluxFlow.StateCompositionSample;

internal sealed class SampleCapture
{
    private readonly object _gate = new();
    private readonly List<StateReducerResult> _stateResults = [];
    private readonly List<FlowCounterSnapshot> _counterSnapshots = [];

    public void Add(StateReducerResult result)
    {
        lock (_gate)
        {
            _stateResults.Add(result);
        }
    }

    public void Add(FlowCounterSnapshot snapshot)
    {
        lock (_gate)
        {
            _counterSnapshots.Add(snapshot);
        }
    }

    public IReadOnlyList<StateReducerResult> GetStateResults()
    {
        lock (_gate)
        {
            return _stateResults.ToArray();
        }
    }

    public IReadOnlyList<FlowCounterSnapshot> GetCounterSnapshots()
    {
        lock (_gate)
        {
            return _counterSnapshots.ToArray();
        }
    }
}
