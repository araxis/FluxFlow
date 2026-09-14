using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Nodes;

public sealed class FlowRetryNode : IFlowComponent
{
    private readonly RetryOperationCoordinator _coordinator;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly Task _completion;
    private int _outputShutdownStarted;
    private int _disposed;

    public FlowRetryNode(FlowRetryOptions? options = null, TimeProvider? clock = null,
        IRetryJitterSource? jitterSource = null)
    {
        var settings = FlowRetryOptionValidation.Validate(options);
        _output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = settings.Capacity });
        _coordinator = new RetryOperationCoordinator(
            settings, EmitAsync, clock, jitterSource);
        _completion = CompleteOutputAsync();
        Ports = new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>>(StringComparer.Ordinal)
            {
                ["Input"] = Input,
                ["Ack"] = Ack,
                ["Nak"] = Nak,
                ["Cancel"] = Cancel
            },
            outputs: new Dictionary<string, ISourceBlock<FlowMessage>>(StringComparer.Ordinal)
            {
                ["Output"] = Output
            },
            events: Events);
        _ = ObserveOutputTerminationAsync();
    }

    public ITargetBlock<FlowMessage> Input => _coordinator.Input;
    public ITargetBlock<FlowMessage> Ack => _coordinator.Ack;
    public ITargetBlock<FlowMessage> Nak => _coordinator.Nak;
    public ITargetBlock<FlowMessage> Cancel => _coordinator.Cancel;
    public ISourceBlock<FlowMessage> Output => _output;
    public ISourceBlock<FlowMessage> Events => _coordinator.Events;
    public FlowPorts Ports { get; }
    public Task Completion => _completion;
    public void Complete() => _coordinator.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _coordinator.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Complete();
        try { await Completion.ConfigureAwait(false); }
        catch { }
        finally
        {
            try { await _coordinator.DisposeAsync().ConfigureAwait(false); }
            finally { await _output.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private async ValueTask<MessageId> EmitAsync(
        RetryEmission emission,
        CancellationToken cancellationToken)
    {
        var output = emission.Error is null
            ? emission.Input.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(emission.Signal!, ValueJsonOptions)),
                emission.Headers, emission.CausationId)
            : emission.Input.WithError(emission.Error,
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
        catch (Exception exception)
        {
            _output.Fault(exception);
            try { await _output.Completion.ConfigureAwait(false); }
            catch { }
            throw;
        }
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
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
