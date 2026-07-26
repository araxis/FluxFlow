using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttControlNode : IFlowNode
{
    private readonly IMqttClientController _controller;
    private readonly TransformBlock<FlowMessage<MqttClientRequest>, FlowMessage<MqttClientResult>> _processor;
    private readonly BroadcastBlock<FlowMessage<MqttClientResult>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
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
        _processor = new TransformBlock<FlowMessage<MqttClientRequest>, FlowMessage<MqttClientResult>>(
            ProcessAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.MaximumPendingRequests,
                MaxDegreeOfParallelism = maximumConcurrency,
                EnsureOrdered = options.ResultOrder == MqttResultOrder.PreserveInput
            });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
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
        ((IDataflowBlock)_processor).Fault(exception);
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

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            await _output.Completion.ConfigureAwait(false);
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
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
}
