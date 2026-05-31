using FluxFlow.Engine.Core;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class FlowNodeBase : IFlowNode, IFlowDiagnosticSource
{
    private readonly BufferBlock<FlowError> _errors = new();
    private readonly FlowFanoutSource<FlowDiagnostic> _diagnostics = new();
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

    public ISourceBlock<FlowDiagnostic> Diagnostics => _diagnostics;

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

        try
        {
            OnNodeCompleted();
        }
        finally
        {
            _errors.Complete();
            _diagnostics.Complete();
        }

        return true;
    }

    protected bool FaultNode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!_completion.TrySetException(exception))
        {
            return false;
        }

        try
        {
            OnNodeFaulted(exception);
        }
        finally
        {
            _errors.Complete();
            _diagnostics.Complete();
        }

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

    protected bool TryEmitDiagnostic(
        string name,
        FlowDiagnosticLevel level = FlowDiagnosticLevel.Information,
        string? message = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
        => _diagnostics.Post(CreateDiagnostic(name, level, message, exception, attributes));

    protected Task<bool> EmitDiagnosticAsync(
        string name,
        FlowDiagnosticLevel level = FlowDiagnosticLevel.Information,
        string? message = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? attributes = null,
        CancellationToken cancellationToken = default)
        => _diagnostics.SendAsync(
            CreateDiagnostic(name, level, message, exception, attributes),
            cancellationToken);

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

    protected virtual FlowDiagnostic CreateDiagnostic(
        string name,
        FlowDiagnosticLevel level = FlowDiagnosticLevel.Information,
        string? message = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new FlowDiagnostic
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = name,
            Level = level,
            Message = message,
            Exception = exception,
            Attributes = attributes ?? new Dictionary<string, object?>(StringComparer.Ordinal)
        };
    }

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
