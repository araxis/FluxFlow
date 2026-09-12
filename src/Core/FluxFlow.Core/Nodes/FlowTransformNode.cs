using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

/// <summary>
/// A bounded one-result processing component. Native transform ordering can preserve
/// input order across concurrent operations. Unexpected exceptions fault the component;
/// implementations return error messages for expected operation failures.
/// </summary>
public abstract class FlowTransformNode : IFlowComponent
{
    private readonly TransformBlock<FlowMessage, FlowMessage> _processor;
    private readonly FlowProcessingLifetime _lifetime;

    protected FlowTransformNode(FlowNodeOptions? options = null, bool ensureOrdered = true)
    {
        options ??= new FlowNodeOptions();
        _lifetime = new FlowProcessingLifetime(options.OutputCapacity);
        _processor = new TransformBlock<FlowMessage, FlowMessage>(RunAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.InputCapacity,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                EnsureOrdered = ensureOrdered
            });
        var forwarder = new ActionBlock<FlowMessage>(
            message => FlowProcessingLifetime.EmitAsync(_lifetime.Output, message, Stopping).AsTask(),
            new ExecutionDataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        _processor.LinkTo(forwarder, new DataflowLinkOptions { PropagateCompletion = true });
        Ports = FlowPorts.CreateStandard(Input, Output, Events);
        _lifetime.Initialize(static () => ValueTask.CompletedTask, _processor, forwarder);
    }

    public ITargetBlock<FlowMessage> Input => _processor;
    public ISourceBlock<FlowMessage> Output => _lifetime.Output;
    public ISourceBlock<FlowMessage> Events => _lifetime.Events;
    public FlowPorts Ports { get; }
    public Task Completion => _lifetime.Completion;
    protected CancellationToken Stopping => _lifetime.Stopping;

    /// <summary>Returns one result; incoming errors bypass this operation unchanged.</summary>
    protected abstract Task<FlowMessage> TransformAsync(FlowMessage message);
    protected bool EmitEvent(FlowEvent @event) => _lifetime.Events.Post(@event);
    protected bool EmitEvent(FlowMessage @event) => _lifetime.Events.Post(@event);
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    public void Complete() => _lifetime.Complete();
    public void Fault(Exception exception) => _lifetime.Fault(exception);
    public ValueTask DisposeAsync() => _lifetime.DisposeAsync(OnDisposeAsync);

    private async Task<FlowMessage> RunAsync(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var activity = FlowTracing.StartProcessing(message, this);
        if (message.IsError) return message;
        try { return await TransformAsync(message).ConfigureAwait(false); }
        catch (Exception error)
        {
            if (error is OperationCanceledException && Stopping.IsCancellationRequested) activity.Canceled();
            else activity.Failed(error);
            // Signal peers before awaiting native completion, which joins active delegates.
            Fault(error);
            throw;
        }
    }
}
