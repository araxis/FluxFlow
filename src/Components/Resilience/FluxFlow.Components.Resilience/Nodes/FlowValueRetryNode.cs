using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Nodes;

internal sealed class FlowValueRetryNode : IFlowNode
{
    private readonly RetryOperationCoordinator<FlowValue, FlowMessage> _coordinator;
    private readonly TransformBlock<FlowMessage, RetryInput<FlowValue, FlowMessage>> _input;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly IDisposable _inputLink;
    private readonly Task _completion;
    private int _outputShutdownStarted;
    private int _disposed;

    public FlowValueRetryNode(FlowRetryOptions? options = null, TimeProvider? clock = null,
        IRetryJitterSource? jitterSource = null)
    {
        var settings = FlowRetryOptionValidation.Validate(options);
        _output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = settings.Capacity });
        _coordinator = new RetryOperationCoordinator<FlowValue, FlowMessage>(
            settings, EmitAsync, clock, jitterSource);
        _input = new TransformBlock<FlowMessage, RetryInput<FlowValue, FlowMessage>>(
            static message => new RetryInput<FlowValue, FlowMessage>(
                message, message.IsError ? null! : message.Value!, message.Error,
                message.CorrelationId, message.TraceId, message.MessageId,
                message.Timestamp, message.Headers),
            InputOptions(settings.Capacity));
        _inputLink = _input.LinkTo(_coordinator.Input,
            new DataflowLinkOptions { PropagateCompletion = true });
        _completion = CompleteOutputAsync();
        Ack = Target(RetryFeedbackKind.Ack);
        Nak = Target(RetryFeedbackKind.Nak);
        Cancel = Target(RetryFeedbackKind.Cancel);
        _ = ObserveOutputTerminationAsync();
    }

    public ITargetBlock<FlowMessage> Input => _input;
    public IFlowSignalTarget Ack { get; }
    public IFlowSignalTarget Nak { get; }
    public IFlowSignalTarget Cancel { get; }
    public ISourceBlock<FlowMessage> Output => _output;
    public ISourceBlock<FlowMessage> Events => _coordinator.Events;
    public Task Completion => _completion;
    public void Complete() => _input.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_input).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Complete();
        try { await Completion.ConfigureAwait(false); }
        catch { }
        finally
        {
            _inputLink.Dispose();
            await _coordinator.DisposeAsync().ConfigureAwait(false);
            await _output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private RetrySignalTarget Target(RetryFeedbackKind kind)
        => new(_coordinator.HandleFeedbackAsync, _completion, kind);

    private async ValueTask<MessageId> EmitAsync(
        RetryEmission<FlowValue, FlowMessage> emission,
        CancellationToken cancellationToken)
    {
        var output = emission.Error is null
            ? emission.Input.Context.With(FlowValue.From(emission.Signal!),
                emission.Headers, emission.CausationId)
            : emission.Input.Context.WithError(emission.Error,
                emission.Headers, emission.CausationId);
        if (!await _output.SendAsync(output, cancellationToken).ConfigureAwait(false))
        {
            await _output.Completion.ConfigureAwait(false);
            throw new InvalidOperationException("Retry output declined an accepted result.");
        }
        return output.MessageId;
    }

    private async Task CompleteOutputAsync()
    {
        try
        {
            await _coordinator.Completion.ConfigureAwait(false);
            Interlocked.Exchange(ref _outputShutdownStarted, 1);
            _output.Complete();
            await _output.Completion.ConfigureAwait(false);
        }
        catch (Exception exception) { _output.Fault(exception); throw; }
    }

    private async Task ObserveOutputTerminationAsync()
    {
        try
        {
            await _output.Completion.ConfigureAwait(false);
            if (Volatile.Read(ref _outputShutdownStarted) == 0 && !_coordinator.Completion.IsCompleted)
                _coordinator.Fault(new InvalidOperationException(
                    "Retry output completed before input processing stopped."));
        }
        catch (Exception exception)
        {
            if (!_completion.IsCompleted) _coordinator.Fault(exception);
        }
    }

    private static ExecutionDataflowBlockOptions InputOptions(int capacity)
        => new() { BoundedCapacity = capacity, EnsureOrdered = true, MaxDegreeOfParallelism = 1 };
}
