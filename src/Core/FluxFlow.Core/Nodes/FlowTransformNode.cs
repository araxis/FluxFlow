using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

/// <summary>
/// A bounded one-result processing component. Native transform ordering can preserve
/// input order across concurrent operations. Processing exceptions become error messages
/// without stopping other messages; explicit faults still stop the component.
/// </summary>
public abstract class FlowTransformNode : IFlowComponent
{
    private readonly TransformBlock<FlowMessage, FlowMessage> _processor;
    private readonly FlowProcessingLifetime _lifetime;

    protected FlowTransformNode(FlowNodeOptions? options = null, bool ensureOrdered = true)
    {
        options ??= new FlowNodeOptions();
        _lifetime = new FlowProcessingLifetime(options.OutputCapacity, this);
        _processor = new TransformBlock<FlowMessage, FlowMessage>(RunAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.InputCapacity,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                EnsureOrdered = ensureOrdered
            });
        Ports = FlowPorts.CreateStandard(Input, Output, Events);
        _lifetime.Initialize(static () => ValueTask.CompletedTask, _processor, ForwardAsync());
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

    private async Task ForwardAsync()
    {
        try
        {
            // Receive one ordered result at a time; no intermediary block or queue.
            await foreach (var message in _processor.ReceiveAllAsync(Stopping).ConfigureAwait(false))
                await FlowProcessingLifetime.EmitAsync(_lifetime.Output, message, Stopping).ConfigureAwait(false);
        }
        catch (Exception error) { _lifetime.Fault(error); }
    }

    private async Task<FlowMessage> RunAsync(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var activity = FlowTracing.StartProcessing(message, this);
        if (message.IsError) return message;
        try { return await TransformAsync(message).ConfigureAwait(false); }
        catch (Exception error)
        {
            if (error is OperationCanceledException && Stopping.IsCancellationRequested)
            {
                activity.Canceled();
                throw;
            }
            activity.Failed(error);
            FlowDiagnostics.ProcessingException(message, this, error);
            return message.WithError(FlowProcessingError.FromException(error));
        }
    }
}
