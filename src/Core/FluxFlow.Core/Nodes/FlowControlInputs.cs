using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;

namespace FluxFlow.Nodes;

/// <summary>Owns terminal control actions, independently of source production.</summary>
internal sealed class FlowControlInputs(object component, Action<Exception> faultOwner) : IDisposable
{
    private readonly Dictionary<string, ITargetBlock<FlowMessage>> _inputs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _aborting = new();
    private readonly object _gate = new();
    private Task? _cancellation;

    internal IReadOnlyDictionary<string, ITargetBlock<FlowMessage>> Inputs => _inputs;

    internal ITargetBlock<FlowMessage> Add(string name,
        Func<FlowMessage, CancellationToken, Task> action, int capacity)
    {
        ApplicationAddress.RequireConstructedSegment(name, nameof(name), "Control port name");
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (_inputs.ContainsKey(name)) throw new ArgumentException("Control port already exists.", nameof(name));
        var block = new ActionBlock<FlowMessage>(async message =>
        {
            using var activity = FlowTracing.StartProcessing(message, component, name);
            try { await action(message, _aborting.Token).ConfigureAwait(false); }
            catch (Exception error)
            {
                if (error is OperationCanceledException && _aborting.IsCancellationRequested) activity.Canceled();
                else activity.Failed(error);
                faultOwner(error);
                throw;
            }
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = capacity });
        _inputs.Add(name, block);
        _ = ObserveAsync(block);
        return block;
    }

    internal void Complete()
    {
        foreach (var input in _inputs.Values) input.Complete();
    }

    internal void Fault(Exception error)
    {
        _ = CancelAsync();
        foreach (var input in _inputs.Values) input.Fault(error);
    }

    internal async Task JoinAsync()
    {
        try { await Task.WhenAll(_inputs.Values.Select(input => input.Completion)).ConfigureAwait(false); }
        finally { await CancelAsync().ConfigureAwait(false); }
    }

    private Task CancelAsync()
    {
        lock (_gate) return _cancellation ??= _aborting.CancelAsync();
    }

    private async Task ObserveAsync(IDataflowBlock input)
    {
        try { await input.Completion.ConfigureAwait(false); }
        catch (Exception error) { faultOwner(error is AggregateException aggregate ? aggregate.GetBaseException() : error); }
    }

    public void Dispose() => _aborting.Dispose();
}
