using FluxFlow.Engine.Core;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class FlowNodeBase : IFlowNode
{
    private readonly BufferBlock<FlowError> _errors = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected FlowNodeBase()
        : this(FlowNodeId.New())
    {
    }

    protected FlowNodeBase(FlowNodeId id)
    {
        Id = id;
    }

    public FlowNodeId Id { get; }

    public Task Completion => _completion.Task;

    public ISourceBlock<FlowError> Errors => _errors;

    public virtual Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual void Complete()
        => CompleteNode();

    public virtual void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        FaultNode(exception);
    }

    protected bool CompleteNode()
    {
        if (!_completion.TrySetResult())
        {
            return false;
        }

        _errors.Complete();
        OnNodeCompleted();
        return true;
    }

    protected bool FaultNode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!_completion.TrySetException(exception))
        {
            return false;
        }

        ((IDataflowBlock)_errors).Fault(exception);
        OnNodeFaulted(exception);
        return true;
    }

    protected void CompleteWhen(Task completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        _ = CompleteWhenAsync(completion);
    }

    protected bool TryReportError(
        int code,
        string message,
        Exception? exception = null,
        string? context = null)
        => _errors.Post(CreateError(code, message, exception, context));

    protected Task<bool> ReportErrorAsync(
        int code,
        string message,
        Exception? exception = null,
        string? context = null,
        CancellationToken cancellationToken = default)
        => _errors.SendAsync(CreateError(code, message, exception, context), cancellationToken);

    protected virtual FlowError CreateError(
        int code,
        string message,
        Exception? exception = null,
        string? context = null)
        => new()
        {
            NodeId = Id,
            Code = code,
            Message = message,
            Exception = exception,
            Context = context
        };

    protected virtual void OnNodeCompleted()
    {
    }

    protected virtual void OnNodeFaulted(Exception exception)
    {
    }

    private async Task CompleteWhenAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
            CompleteNode();
        }
        catch (Exception exception)
        {
            FaultNode(exception);
        }
    }
}
