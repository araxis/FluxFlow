using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttPublishOperationNode : IFlowNode
{
    private readonly IMqttClientController _controller;
    private readonly TransformBlock<FlowMessage<MqttPublishMessage>, FlowMessage<MqttClientResult>> _processor;
    private readonly BroadcastBlock<FlowMessage<MqttClientResult>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public MqttPublishOperationNode(
        IMqttClientController controller,
        int maximumPendingRequests = 128)
    {
        if (maximumPendingRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _processor = new TransformBlock<FlowMessage<MqttPublishMessage>, FlowMessage<MqttClientResult>>(
            ProcessAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = maximumPendingRequests,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<MqttPublishMessage>> Input => _processor;

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
        FlowMessage<MqttPublishMessage> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var result = await _controller.ExecuteAsync(new MqttPublishClientRequest
        {
            Message = message.Payload
        }).ConfigureAwait(false);
        _events.Post(new FlowEvent
        {
            Timestamp = result.Timestamp,
            CorrelationId = message.CorrelationId,
            Name = result.IsError ? "mqtt.publish.failed" : "mqtt.publish.completed",
            Level = result.IsError ? FlowEventLevel.Warning : FlowEventLevel.Information,
            Message = result.IsError ? result.Error!.Message : null,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["client"] = _controller.Name,
                ["topic"] = message.Payload.Topic,
                ["qos"] = message.Payload.Qos.ToString(),
                ["retain"] = message.Payload.Retain
            }
        });
        return message.With(result);
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
}
