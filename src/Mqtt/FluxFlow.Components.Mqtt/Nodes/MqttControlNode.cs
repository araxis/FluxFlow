using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttControlNode : IFlowNode
{
    private readonly IMqttClientController _controller;
    private readonly TransformBlock<FlowMessage<MqttClientRequest>, FlowMessage<MqttClientResult>> _processor;
    private readonly ActionBlock<FlowMessage<MqttClientResult>> _forwarder;
    private readonly FlowOutput<FlowMessage<MqttClientResult>> _output;
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public MqttControlNode(
        IMqttClientController controller,
        MqttControlOptions? options = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        options ??= new MqttControlOptions();
        ValidateOptions(options);

        var maximumConcurrency = options.RequestProcessing == MqttRequestProcessing.Sequential
            ? 1
            : options.MaximumConcurrentRequests;
        _output = new FlowOutput<FlowMessage<MqttClientResult>>(
            new FlowOutputOptions { Capacity = options.MaximumPendingRequests });
        _processor = new TransformBlock<FlowMessage<MqttClientRequest>, FlowMessage<MqttClientResult>>(
            ProcessAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.MaximumPendingRequests,
                MaxDegreeOfParallelism = maximumConcurrency,
                EnsureOrdered = options.ResultOrder == MqttResultOrder.PreserveInput
            });
        _forwarder = new ActionBlock<FlowMessage<MqttClientResult>>(
            ForwardAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.MaximumPendingRequests,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _processor.LinkTo(_forwarder, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<MqttClientRequest>> Input => _processor;

    public ISourceBlock<FlowMessage<MqttClientResult>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void Complete() => _processor.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        TransitionToFault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative unexpected-fault surface.
        }
        finally
        {
            _stopping.Cancel();
            await _output.DisposeAsync().ConfigureAwait(false);
            _stopping.Dispose();
        }
    }

    private async Task<FlowMessage<MqttClientResult>> ProcessAsync(
        FlowMessage<MqttClientRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<MqttClientResult>(message.Error!);

        try
        {
            var result = await _controller.ExecuteAsync(message.Value).ConfigureAwait(false);
            _events.Post(new FlowEvent
            {
                Timestamp = result.Timestamp,
                CorrelationId = message.CorrelationId,
                Name = "mqtt.command.completed",
                Level = FlowEventLevel.Information,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["operation"] = result.Operation.ToString(),
                    ["kind"] = result.Kind
                }
            });
            return message.With(result);
        }
        catch (MqttClientOperationException exception)
        {
            _events.Post(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Name = "mqtt.command.failed",
                Level = FlowEventLevel.Warning,
                Message = exception.Error.Message,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["operation"] = exception.Operation.ToString(),
                    ["errorCode"] = exception.Error.Code
                }
            });
            return message.WithError<MqttClientResult>(exception.Error);
        }
    }

    private async Task ForwardAsync(FlowMessage<MqttClientResult> message)
    {
        if (await _output.SendAsync(message, _stopping.Token).ConfigureAwait(false))
            return;

        await _output.Completion.ConfigureAwait(false);
        throw new InvalidOperationException("mqtt control output declined a result.");
    }

    private async Task MonitorCompletionAsync()
    {
        try
        {
            var first = await Task.WhenAny(_forwarder.Completion, _output.Completion)
                .ConfigureAwait(false);
            if (ReferenceEquals(first, _output.Completion))
            {
                await _output.Completion.ConfigureAwait(false);
                if (!_forwarder.Completion.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "mqtt control output completed before input processing stopped.");
                }
            }

            await _forwarder.Completion.ConfigureAwait(false);
            _output.Complete();
            _events.Complete();
            await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            TransitionToFault(Unwrap(exception));
        }
    }

    private void TransitionToFault(Exception exception)
    {
        _stopping.Cancel();
        ((IDataflowBlock)_processor).Fault(exception);
        ((IDataflowBlock)_forwarder).Fault(exception);
        _output.Fault(exception);
        _events.Complete();
        _completion.TrySetException(exception);
    }

    private static void ValidateOptions(MqttControlOptions options)
    {
        if (!Enum.IsDefined(options.RequestProcessing))
            throw new ArgumentOutOfRangeException(nameof(options.RequestProcessing));
        if (!Enum.IsDefined(options.ResultOrder))
            throw new ArgumentOutOfRangeException(nameof(options.ResultOrder));
        if (options.MaximumConcurrentRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumConcurrentRequests));
        if (options.MaximumPendingRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumPendingRequests));
    }

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerException!
            : exception;
}
