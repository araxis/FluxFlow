using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

internal sealed class FlowValueTimerSourceProjection<TTick> : IFlowSource
{
    private readonly IFlowSource _source;
    private readonly TransformBlock<FlowMessage<TTick>, FlowMessage<FlowValue>> _projection;
    private readonly BroadcastBlock<FlowMessage<FlowValue>> _output;
    private readonly Task _completion;
    private int _disposed;

    public FlowValueTimerSourceProjection(
        IFlowSource source,
        ISourceBlock<FlowMessage<TTick>> sourceOutput,
        ISourceBlock<FlowEvent> events,
        Func<TTick, FlowValue> convert,
        int boundedCapacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceOutput);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(convert);
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);

        _source = source;
        Events = events;
        _projection = new TransformBlock<FlowMessage<TTick>, FlowMessage<FlowValue>>(
            message => new FlowMessage<FlowValue>(
                message.CorrelationId,
                convert(message.Payload))
            {
                TraceId = message.TraceId,
                MessageId = message.MessageId,
                CausationId = message.CausationId,
                Timestamp = message.Timestamp,
                Headers = message.Headers
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = boundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _output = new BroadcastBlock<FlowMessage<FlowValue>>(
            static message => message,
            new DataflowBlockOptions { BoundedCapacity = boundedCapacity });
        sourceOutput.LinkTo(_projection, new DataflowLinkOptions { PropagateCompletion = true });
        _projection.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _completion = MonitorCompletionAsync();
    }

    public ISourceBlock<FlowMessage<FlowValue>> Output => _output;

    public ISourceBlock<FlowEvent> Events { get; }

    public Task Completion => _completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _source.StartAsync(cancellationToken);

    public void Complete() => _source.Complete();

    public void Fault(Exception exception) => _source.Fault(exception);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _source.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative unexpected-fault surface.
        }
    }

    private async Task MonitorCompletionAsync()
    {
        await _source.Completion.ConfigureAwait(false);
        await _projection.Completion.ConfigureAwait(false);
        await _output.Completion.ConfigureAwait(false);
    }
}
