using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using NodeFlowError = FluxFlow.Nodes.FlowError;

namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FlowValueFileSystemSourceProjection<TSource> : IFlowSource
{
    private readonly IFlowSource _source;
    private readonly TransformBlock<FlowMessage<TSource>, FlowMessage<FlowValue>> _projection;
    private readonly BroadcastBlock<FlowMessage<FlowValue>> _output;
    private readonly ActionBlock<NodeFlowError> _errorObserver;
    private readonly Task _completion;
    private int _disposed;

    public FlowValueFileSystemSourceProjection(
        IFlowSource source,
        ISourceBlock<FlowMessage<TSource>> sourceOutput,
        ISourceBlock<NodeFlowError> sourceErrors,
        ISourceBlock<FlowEvent> events,
        Func<TSource, FlowValue> convert,
        int boundedCapacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceOutput);
        ArgumentNullException.ThrowIfNull(sourceErrors);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(convert);
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);

        _source = source;
        Events = events;
        _projection = new TransformBlock<FlowMessage<TSource>, FlowMessage<FlowValue>>(
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
        _errorObserver = new ActionBlock<NodeFlowError>(PromoteSourceFailure);

        sourceOutput.LinkTo(_projection, new DataflowLinkOptions { PropagateCompletion = true });
        _projection.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        sourceErrors.LinkTo(_errorObserver, new DataflowLinkOptions { PropagateCompletion = true });
        _completion = Task.WhenAll(
            _source.Completion,
            _projection.Completion,
            _output.Completion,
            _errorObserver.Completion);
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
            // Completion remains the authoritative isolated-fault surface.
        }
    }

    private void PromoteSourceFailure(NodeFlowError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var exception = new FileSystemSourceException(error);
        _source.Fault(exception);
        throw exception;
    }

    private sealed class FileSystemSourceException : IOException
    {
        public FileSystemSourceException(NodeFlowError error)
            : base(error.Message, error.Exception)
        {
            ErrorCode = error.Code;
            Context = error.Context;
        }

        public int ErrorCode { get; }

        public string? Context { get; }
    }
}
