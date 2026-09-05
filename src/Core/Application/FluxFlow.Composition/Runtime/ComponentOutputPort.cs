using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public abstract class ComponentOutputPort
{
    private protected ComponentOutputPort(string name, Type messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
    }

    public string Name { get; }

    public Type MessageType { get; }

    internal abstract Task Completion { get; }

    internal abstract bool TryLinkTo(ComponentInputPort input, out IDisposable? link);
}

public sealed class ComponentOutputPort<TMessage> : ComponentOutputPort
{
    public ComponentOutputPort(string name, ISourceBlock<FlowMessage<TMessage>> source)
        : base(name, typeof(TMessage))
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public ISourceBlock<FlowMessage<TMessage>> Source { get; }

    internal override Task Completion => Source.Completion;

    internal override bool TryLinkTo(ComponentInputPort input, out IDisposable? link)
    {
        if (input is ComponentSignalInputPort signalInput)
        {
            link = new CompositionSignalLink<TMessage>(Source, signalInput.Target);
            return true;
        }

        if (input is not ComponentInputPort<TMessage> typedInput)
        {
            link = null;
            return false;
        }

        link = Source.LinkTo(
            typedInput.Target,
            new DataflowLinkOptions { PropagateCompletion = false });
        return true;
    }
}

public sealed class FlowValueComponentOutputPort : ComponentOutputPort
{
    public FlowValueComponentOutputPort(string name, ISourceBlock<FlowMessage> source)
        : base(name, typeof(global::FluxFlow.Data.FlowValue))
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public ISourceBlock<FlowMessage> Source { get; }

    internal override Task Completion => Source.Completion;

    internal override bool TryLinkTo(ComponentInputPort input, out IDisposable? link)
    {
        if (input is ComponentSignalInputPort signalInput)
        {
            link = new FlowValueCompositionSignalLink(Source, signalInput.Target);
            return true;
        }

        if (input is not FlowValueComponentInputPort flowValueInput)
        {
            link = null;
            return false;
        }

        link = Source.LinkTo(
            flowValueInput.Target,
            new DataflowLinkOptions { PropagateCompletion = false });
        return true;
    }
}

internal sealed class FlowValueCompositionSignalLink : IDisposable
{
    private readonly ActionBlock<FlowMessage> _forwarder;
    private readonly IDisposable _sourceLink;
    private int _disposed;

    public FlowValueCompositionSignalLink(
        ISourceBlock<FlowMessage> source,
        IFlowSignalTarget target)
    {
        _forwarder = new ActionBlock<FlowMessage>(
            async message => await target.SendAsync(message).ConfigureAwait(false),
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _sourceLink = source.LinkTo(
            _forwarder,
            new DataflowLinkOptions { PropagateCompletion = true });

        _ = target.Completion.ContinueWith(
            static (_, state) => ((ActionBlock<FlowMessage>)state!).Complete(),
            _forwarder,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _sourceLink.Dispose();
        _forwarder.Complete();
    }
}

internal sealed class CompositionSignalLink<TMessage> : IDisposable
{
    private readonly ActionBlock<FlowMessage<TMessage>> _forwarder;
    private readonly IDisposable _sourceLink;
    private int _disposed;

    public CompositionSignalLink(
        ISourceBlock<FlowMessage<TMessage>> source,
        IFlowSignalTarget target)
    {
        _forwarder = new ActionBlock<FlowMessage<TMessage>>(
            async message =>
            {
                await target.SendAsync(message).ConfigureAwait(false);
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _sourceLink = source.LinkTo(
            _forwarder,
            new DataflowLinkOptions { PropagateCompletion = true });

        _ = target.Completion.ContinueWith(
            static (_, state) => ((ActionBlock<FlowMessage<TMessage>>)state!).Complete(),
            _forwarder,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _sourceLink.Dispose();
        _forwarder.Complete();
    }
}
